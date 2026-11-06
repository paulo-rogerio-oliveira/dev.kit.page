using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A provisão do lado da API: grava o pedido e a auditoria, e lê — sempre filtrando pelo usuário do token. Nada aqui fala
/// com o Azure: quem executa é o worker, pelo <see cref="MotorDeProvisao"/>.
/// <para>
/// A COTA é atômica no banco (a ressalva do item 4): o pedido roda numa transação que primeiro TRAVA o usuário (uma
/// atualização na linha dele, que segura o pedido simultâneo do mesmo usuário até o fim desta) e só então conta os ativos,
/// confere o teto do custo e grava; e cada recurso ocupa uma VAGA da cota com índice único filtrado — mesmo sem a trava,
/// dois pedidos não ocupam a mesma vaga.
/// </para>
/// </summary>
public sealed class Provisao(DevKitPageDb db, TimeProvider relogio, IOptions<OpcoesDaProvisao> opcoes, ISegundoFator segundoFator) : IProvisao
{
    /// <summary>Quantas vezes o pedido é refeito quando o índice único barra a gravação (o nome sorteado repetido ou a vaga tomada).</summary>
    private const int TentativasDeGravacao = 3;

    public async Task<PermissaoDeProvisaoV1> PermissaoAsync(int usuarioId, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        var origem = await OrigemAsync(db, cfg, usuarioId, ct);
        return new PermissaoDeProvisaoV1(origem != ProvisaoV1.OrigemNenhuma, origem, RegrasDaProvisao.Regioes(cfg), RegrasDaProvisao.RegiaoPadrao(cfg),
            RegrasDaProvisao.Maximo(ProvisaoV1.TipoFila, cfg), RegrasDaProvisao.Aplicativos(cfg), Math.Max(0m, cfg.LimiteDeCustoMensal),
            Math.Clamp(cfg.AlertaDeCustoPercentual, 1, 100));
    }

    public async Task<CotaDaProvisaoV1> CotaAsync(int usuarioId, CancellationToken ct)
        => RegrasDaProvisao.Cota((await AtivosAsync(usuarioId, ct)).Select(r => (r.Tipo, r.CustoMensal)), opcoes.Value);

    /// <summary>De onde vem a liberação do usuário: a permissão explícita, o plano da empresa, ou nenhuma.</summary>
    internal static async Task<string> OrigemAsync(DevKitPageDb db, OpcoesDaProvisao cfg, int usuarioId, CancellationToken ct)
    {
        var permissao = await db.PermissoesDeUsuario.AsNoTracking().AnyAsync(p => p.UsuarioId == usuarioId && p.Recurso == ProvisaoV1.RecursoProvisao, ct);
        var plano = permissao || cfg.PlanosQueLiberam.Length == 0
            ? null
            : await db.Usuarios.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.Empresa == null ? null : u.Empresa.Plano).FirstOrDefaultAsync(ct);
        return RegrasDaProvisao.Origem(permissao, plano, cfg.PlanosQueLiberam);
    }

    public async Task<IReadOnlyList<RecursoProvisionadoV1>> RecursosAsync(int usuarioId, CancellationToken ct)
        => (await db.RecursosProvisionados.AsNoTracking().Where(r => r.UsuarioId == usuarioId).ToListAsync(ct))
            .OrderByDescending(r => r.CriadoEmUtc).ThenByDescending(r => r.Id).Select(Leitura).ToArray();

    public async Task<DetalheDoRecursoV1?> DetalheAsync(int usuarioId, long id, CancellationToken ct)
    {
        var recurso = await db.RecursosProvisionados.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuarioId, ct);
        if (recurso is null)
            return null;
        var eventos = (await db.EventosDaProvisao.AsNoTracking().Where(e => e.RecursoId == id).ToListAsync(ct))
            .OrderBy(e => e.EmUtc).ThenBy(e => e.Id)
            .Select(e => new EventoDaProvisaoV1(DevKitPageDb.Utc(e.EmUtc), e.Evento, e.Autor, e.Detalhe)).ToArray();
        return new DetalheDoRecursoV1(Leitura(recurso), eventos);
    }

    public Task<ResultadoDaGestao<PedidoAceito>> PedirFilaAsync(int usuarioId, string autor, string regiao, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        return PedirAsync(usuarioId, ct, async (ativos, agora) =>
        {
            var filas = ativos.Where(r => r.Tipo == ProvisaoV1.TipoFila).OrderByDescending(r => r.CriadoEmUtc).ThenByDescending(r => r.Id).ToList();
            // Cota cheia: devolve a fila que já existe — o duplo clique e o reenvio não duplicam recurso pago.
            if (RegrasDaProvisao.Vaga(filas.Select(r => r.Vaga), RegrasDaProvisao.Maximo(ProvisaoV1.TipoFila, cfg)) is not { } vaga)
                return new(new PedidoAceito(Leitura(filas[0]), Novo: false));

            // O nome é sorteado AQUI, uma vez: a repetição do pedido usa o mesmo, e o índice único barra o sorteio repetido.
            var recurso = Novo(usuarioId, ProvisaoV1.TipoFila, vaga, RegrasDaProvisao.NomeDaConta(usuarioId), regiao, agora, cfg);
            recurso.Fila = ProvisaoV1.NomeDaFila;
            db.RecursosProvisionados.Add(recurso);
            await db.SaveChangesAsync(ct);
            await AuditarAsync(recurso, ProvisaoV1.EventoPedido, autor, $"{recurso.Regiao} · conta {recurso.Nome} · grupo {recurso.GrupoDeRecursos}", agora, ct);
            return new(new PedidoAceito(Leitura(recurso), Novo: true));
        });
    }

    public Task<ResultadoDaGestao<PedidoAceito>> PedirAplicativoAsync(int usuarioId, string autor, AplicativoPedido pedido, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        var tipo = RegrasDaProvisao.RotuloDoTipo(pedido.Tipo);
        return PedirAsync(usuarioId, ct, async (ativos, agora) =>
        {
            // O mesmo aplicativo pedido de novo (o duplo clique, o reenvio) volta como está.
            if (ativos.FirstOrDefault(r => r.Tipo == pedido.Tipo && r.Rotulo == pedido.Rotulo) is { } mesmo)
                return new(new PedidoAceito(Leitura(mesmo), Novo: false));

            var doTipo = ativos.Where(r => r.Tipo == pedido.Tipo).ToList();
            var maximo = RegrasDaProvisao.Maximo(pedido.Tipo, cfg);
            if (RegrasDaProvisao.Vaga(doTipo.Select(r => r.Vaga), maximo) is not { } vaga)
                return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito,
                    $"A cota de {tipo} está cheia ({doTipo.Count} de {maximo}). Remova um para criar outro.", ProvisaoV1.CodigoCotaCheia);

            var custoAntes = ativos.Where(r => RegrasDaProvisao.EhAplicativo(r.Tipo)).Sum(r => r.CustoMensal);
            if (RegrasDaProvisao.PassaDoTeto(custoAntes, pedido.Plano.CustoMensal, cfg))
                return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito,
                    $"O custo estimado do mês ({Reais(custoAntes)}) com o plano {pedido.Plano.Rotulo} ({Reais(pedido.Plano.CustoMensal)}) passaria do seu limite de {Reais(cfg.LimiteDeCustoMensal)}. "
                    + "Remova um aplicativo ou escolha um plano gratuito.", ProvisaoV1.CodigoCustoNoLimite);

            var sufixo = RegrasDaProvisao.Sufixo();
            var recurso = Novo(usuarioId, pedido.Tipo, vaga, RegrasDaProvisao.NomeDoAplicativo(pedido.Tipo, usuarioId, pedido.Rotulo, sufixo), pedido.Regiao, agora, cfg);
            recurso.Rotulo = pedido.Rotulo;
            recurso.Plano = pedido.Plano.Id;
            recurso.CustoMensal = pedido.Plano.CustoMensal;
            if (pedido.Tipo == ProvisaoV1.TipoAppService)
                recurso.NomeDoPlano = RegrasDaProvisao.NomeDoPlanoDoServico(usuarioId, pedido.Rotulo, sufixo);
            db.RecursosProvisionados.Add(recurso);
            await db.SaveChangesAsync(ct);

            await AuditarAsync(recurso, ProvisaoV1.EventoPedido, autor,
                $"{tipo} {recurso.Plano} em {recurso.Regiao} · {recurso.Nome} · cota reservada ({doTipo.Count + 1} de {maximo})", agora, ct);
            var custoDepois = custoAntes + recurso.CustoMensal;
            if (!RegrasDaProvisao.EmAlerta(custoAntes, cfg) && RegrasDaProvisao.EmAlerta(custoDepois, cfg))
                await AuditarAsync(recurso, ProvisaoV1.EventoAlertaDeCusto, autor,
                    $"{Math.Clamp(cfg.AlertaDeCustoPercentual, 1, 100)}% do limite do mês: {Reais(custoDepois)} de {Reais(cfg.LimiteDeCustoMensal)}", agora, ct);
            return new(new PedidoAceito(Leitura(recurso), Novo: true));
        });
    }

    /// <summary>
    /// O pedido com a cota ATÔMICA: numa transação, trava o usuário, lê os recursos ativos dele e entrega à
    /// <paramref name="decidir"/>, que grava (ou recusa); o índice único que barrar a gravação (o nome sorteado repetido, a
    /// vaga tomada) refaz tudo do começo, relendo.
    /// </summary>
    private async Task<ResultadoDaGestao<PedidoAceito>> PedirAsync(int usuarioId, CancellationToken ct,
        Func<List<RecursoProvisionado>, DateTime, Task<ResultadoDaGestao<PedidoAceito>>> decidir)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            await using var transacao = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await TravarAsync(usuarioId, ct);
                var resultado = await decidir(await AtivosAsync(usuarioId, ct, rastrear: true), agora);
                await transacao.CommitAsync(ct);
                return resultado;
            }
            catch (DbUpdateException) when (tentativa < TentativasDeGravacao)
            {
                await transacao.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// Trava o usuário até o fim da transação: uma atualização sem efeito na linha dele. No SQL Server ela segura o bloqueio
    /// exclusivo da linha; no SQLite, a escrita da base. O segundo pedido do mesmo usuário espera aqui, e então lê os ativos
    /// já com o primeiro gravado.
    /// </summary>
    private Task TravarAsync(int usuarioId, CancellationToken ct)
        => db.Usuarios.Where(u => u.Id == usuarioId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Login, u => u.Login), ct);

    /// <summary>Os recursos não removidos do usuário (o que conta na cota e no custo).</summary>
    private Task<List<RecursoProvisionado>> AtivosAsync(int usuarioId, CancellationToken ct, bool rastrear = false)
    {
        var consulta = db.RecursosProvisionados.Where(r => r.UsuarioId == usuarioId && r.Estado != ProvisaoV1.EstadoRemovido);
        return (rastrear ? consulta : consulta.AsNoTracking()).ToListAsync(ct);
    }

    private static RecursoProvisionado Novo(int usuarioId, string tipo, int vaga, string nome, string regiao, DateTime agora, OpcoesDaProvisao cfg) => new()
    {
        UsuarioId = usuarioId,
        Tipo = tipo,
        Vaga = vaga,
        Nome = nome,
        Regiao = regiao,
        GrupoDeRecursos = RegrasDaProvisao.NomeDoGrupo(cfg.PrefixoDoGrupo, usuarioId),
        Operacao = ProvisaoV1.OperacaoCriar,
        Estado = ProvisaoV1.EstadoPedido,
        CriadoEmUtc = agora,
        AtualizadoEmUtc = agora,
    };

    /// <summary>"R$ 1.234,50" — montado à mão: a API roda em globalização invariante, sem a cultura pt-BR.</summary>
    internal static string Reais(decimal valor)
    {
        var invariante = valor.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture);
        return "R$ " + string.Concat(invariante.Select(c => c switch { ',' => '.', '.' => ',', _ => c }));
    }

    public async Task<ResultadoDaGestao<PedidoAceito>> RepetirAsync(int usuarioId, string autor, long id, CancellationToken ct)
    {
        var recurso = await db.RecursosProvisionados.FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuarioId, ct);
        if (recurso is null)
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.NaoEncontrado, "Recurso não encontrado.");
        if (!RegrasDaProvisao.PodeRepetir(recurso.Estado))
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito, "Só o pedido que falhou se repete.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        // A falha parcial retoma do passo que faltou: os passos feitos ficam (o plano não é criado de novo).
        var retoma = RegrasDaProvisao.Parcial(recurso.Estado, recurso.Operacao, recurso.Passos)
            ? $" · retoma depois de: {string.Join(", ", RegrasDaProvisao.Passos(recurso.Passos))}"
            : string.Empty;
        Reabrir(recurso, recurso.Operacao, agora);
        if (!await GravarAsync(ct))
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito, "O pedido mudou enquanto você o repetia. Atualize e tente de novo.");
        await AuditarAsync(recurso, ProvisaoV1.EventoRepeticao, autor, RotuloDaOperacao(recurso.Operacao) + retoma, agora, ct);
        return new(new PedidoAceito(Leitura(recurso), Novo: true));
    }

    public async Task<ResultadoDaGestao<PedidoAceito>> RemoverAsync(int usuarioId, string autor, long id, string? codigo, CancellationToken ct)
    {
        var recurso = await db.RecursosProvisionados.FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuarioId, ct);
        if (recurso is null)
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.NaoEncontrado, "Recurso não encontrado.");

        var aplicativo = RegrasDaProvisao.EhAplicativo(recurso.Tipo);
        switch (RegrasDaProvisao.DecidirRemocao(recurso.Estado, recurso.Operacao))
        {
            case Remocao.JaFeita:
            case Remocao.JaPedida:
                return new(new PedidoAceito(Leitura(recurso), Novo: false));
            case Remocao.AguardeACriacao:
                return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito, aplicativo
                    ? $"O {RegrasDaProvisao.RotuloDoTipo(recurso.Tipo)} ainda está sendo criado. Remova depois que ele ficar pronto (ou falhar)."
                    : "A fila ainda está sendo criada. Remova depois que ela ficar pronta (ou falhar).");
        }

        // O aplicativo só sai com o segundo fator: o código do autenticador, conferido (e gasto) antes de pedir a remoção.
        if (RegrasDaProvisao.RemocaoPedeSegundoFator(recurso.Tipo))
        {
            var recusa = string.IsNullOrWhiteSpace(codigo)
                ? "Remover um aplicativo pede o segundo fator: informe o código de 6 dígitos do seu aplicativo autenticador."
                : !await segundoFator.AtivoAsync(usuarioId, ct)
                    ? "Configure o aplicativo autenticador antes de remover: é o mesmo segundo fator do Aprovar remoto."
                    : await segundoFator.VerificarAsync(usuarioId, codigo, ativar: false, ct);
            if (recusa is not null)
                return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Proibida, recusa, ProvisaoV1.CodigoSegundoFator);
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        Reabrir(recurso, ProvisaoV1.OperacaoRemover, agora);
        if (!await GravarAsync(ct))
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito, "O pedido mudou enquanto você o removia. Atualize e tente de novo.");
        var detalhe = !aplicativo
            ? $"conta {recurso.Nome}"
            : $"{RegrasDaProvisao.RotuloDoTipo(recurso.Tipo)} {recurso.Nome}"
              + (recurso.NomeDoPlano.Length > 0 ? $" e o plano {recurso.NomeDoPlano}" : string.Empty) + " · segundo fator conferido";
        await AuditarAsync(recurso, ProvisaoV1.EventoRemocaoPedida, autor, detalhe, agora, ct);
        return new(new PedidoAceito(Leitura(recurso), Novo: true));
    }

    /// <summary>Devolve o recurso ao worker com a operação, do começo das tentativas.</summary>
    private static void Reabrir(RecursoProvisionado recurso, string operacao, DateTime agora)
    {
        recurso.Operacao = operacao;
        recurso.Estado = ProvisaoV1.EstadoPedido;
        recurso.Tentativas = 0;
        recurso.Erro = string.Empty;
        recurso.ProximaTentativaEmUtc = null;
        recurso.ReservadoPor = string.Empty;
        recurso.ReservadoAteUtc = null;
        recurso.Versao++;
        recurso.AtualizadoEmUtc = agora;
    }

    /// <summary>Grava a mudança; falso quando o worker mudou o recurso no meio (o token de concorrência).</summary>
    private async Task<bool> GravarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private Task AuditarAsync(RecursoProvisionado recurso, string evento, string autor, string detalhe, DateTime agora, CancellationToken ct)
        => MotorDeProvisao.AuditarAsync(db, recurso.Id, recurso.UsuarioId, evento, autor, detalhe, agora, ct);

    internal static string RotuloDaOperacao(string operacao) => operacao == ProvisaoV1.OperacaoRemover ? "remoção" : "criação";

    internal static RecursoProvisionadoV1 Leitura(RecursoProvisionado r)
        => new(r.Id, r.Tipo, r.Nome, r.Fila, r.Regiao, r.GrupoDeRecursos, r.Operacao, r.Estado, r.Tentativas, r.Erro, r.Endpoint,
            DevKitPageDb.Utc(r.CriadoEmUtc), DevKitPageDb.Utc(r.AtualizadoEmUtc),
            r.ProntoEmUtc is { } pronto ? DevKitPageDb.Utc(pronto) : null,
            r.ProximaTentativaEmUtc is { } proxima ? DevKitPageDb.Utc(proxima) : null,
            r.Rotulo, r.Plano, r.NomeDoPlano, r.CustoMensal, RegrasDaProvisao.Passos(r.Passos),
            RegrasDaProvisao.Parcial(r.Estado, r.Operacao, r.Passos));
}

/// <summary>
/// O motor da provisão, usado pelo WORKER (<c>DevKitPage.Provisao</c>) sobre a mesma base. A reserva é otimista: o worker
/// marca o pedido com o nome dele e uma validade, e o token de concorrência faz o segundo worker perder a corrida.
/// </summary>
public sealed class MotorDeProvisao(DevKitPageDb db, TimeProvider relogio, IOptions<OpcoesDaProvisao> opcoes) : IMotorDeProvisao
{
    public async Task<RecursoProvisionado?> ReservarAsync(string trabalhador, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        var agora = relogio.GetUtcNow().UtcDateTime;
        // O pedido vencido e a execução cuja reserva venceu (o worker caiu no meio): os mais antigos primeiro.
        var candidatos = await db.RecursosProvisionados
            .Where(r => (r.Estado == ProvisaoV1.EstadoPedido && (r.ProximaTentativaEmUtc == null || r.ProximaTentativaEmUtc <= agora))
                || (r.Estado == ProvisaoV1.EstadoEmProvisao && r.ReservadoAteUtc != null && r.ReservadoAteUtc < agora))
            .OrderBy(r => r.AtualizadoEmUtc).ThenBy(r => r.Id)
            .Take(10)
            .ToListAsync(ct);

        foreach (var recurso in candidatos)
        {
            var retomada = recurso.Estado == ProvisaoV1.EstadoEmProvisao;
            if (retomada && recurso.Tentativas >= Math.Max(1, cfg.MaximoDeTentativas))
            {
                // O worker caiu no meio em TODAS as tentativas: o pedido não volta a derrubá-lo — fica falhou, e o usuário repete.
                await DesistirAsync(recurso, agora, ct);
                continue;
            }

            recurso.Estado = ProvisaoV1.EstadoEmProvisao;
            recurso.Tentativas++;
            recurso.ReservadoPor = trabalhador;
            recurso.ReservadoAteUtc = agora.AddMinutes(Math.Max(1, cfg.ReservaMinutos));
            recurso.ProximaTentativaEmUtc = null;
            recurso.Versao++;
            recurso.AtualizadoEmUtc = agora;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outro worker (ou o usuário) mudou o pedido entre a leitura e a gravação: o próximo.
                db.Entry(recurso).State = EntityState.Detached;
                continue;
            }

            await AuditarAsync(db, recurso.Id, recurso.UsuarioId, ProvisaoV1.EventoInicio, ProvisaoV1.AutorWorker,
                $"{Provisao.RotuloDaOperacao(recurso.Operacao)} · tentativa {recurso.Tentativas}{(retomada ? " (retomada: a reserva anterior venceu)" : string.Empty)}",
                agora, ct);
            return recurso;
        }

        return null;
    }

    public async Task<bool> ConcluirAsync(long id, string trabalhador, RecursoNaNuvem? recursoNaNuvem, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var recurso = await DoTrabalhadorAsync(id, trabalhador, ct);
        if (recurso is null)
            return false;

        var remocao = recurso.Operacao == ProvisaoV1.OperacaoRemover;
        recurso.Estado = remocao ? ProvisaoV1.EstadoRemovido : ProvisaoV1.EstadoPronto;
        recurso.Erro = string.Empty;
        if (remocao)
        {
            recurso.RemovidoEmUtc = agora;
            recurso.Endpoint = string.Empty;
            // Nada ficou no Azure: a vaga da cota volta (o índice filtra os removidos) e o custo deixa de contar.
            recurso.Passos = string.Empty;
        }
        else
        {
            recurso.ProntoEmUtc = agora;
            recurso.IdNoAzure = recursoNaNuvem?.IdNoAzure ?? recurso.IdNoAzure;
            recurso.Endpoint = recursoNaNuvem?.Endpoint ?? recurso.Endpoint;
        }

        if (!await LiberarAsync(recurso, agora, ct))
            return false;
        await AuditarAsync(db, recurso.Id, recurso.UsuarioId, remocao ? ProvisaoV1.EventoRemovido : ProvisaoV1.EventoPronto, ProvisaoV1.AutorWorker,
            Desfecho(recurso, remocao), agora, ct);
        return true;
    }

    /// <summary>O que a auditoria diz do recurso pronto ou removido, pelo tipo.</summary>
    private static string Desfecho(RecursoProvisionado r, bool remocao) => (r.Tipo, remocao) switch
    {
        (ProvisaoV1.TipoFila, true) => $"conta {r.Nome} removida",
        (ProvisaoV1.TipoFila, false) => $"grupo, conta e fila {r.Fila} prontos",
        (ProvisaoV1.TipoAppService, true) => $"aplicativo {r.Nome} e plano {r.NomeDoPlano} removidos",
        (ProvisaoV1.TipoAppService, false) => $"plano {r.NomeDoPlano} e aplicativo {r.Nome} prontos",
        (_, true) => $"{RegrasDaProvisao.RotuloDoTipo(r.Tipo)} {r.Nome} removido",
        _ => $"{RegrasDaProvisao.RotuloDoTipo(r.Tipo)} {r.Nome} pronto",
    };

    public async Task<bool> RegistrarPassoAsync(long id, string trabalhador, string passo, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var recurso = await DoTrabalhadorAsync(id, trabalhador, ct);
        if (recurso is null)
            return false;

        recurso.Passos = RegrasDaProvisao.ComPasso(recurso.Passos, passo);
        recurso.Versao++;
        recurso.AtualizadoEmUtc = agora;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(recurso).State = EntityState.Detached;
            return false;
        }

        await AuditarAsync(db, recurso.Id, recurso.UsuarioId, ProvisaoV1.EventoPasso, ProvisaoV1.AutorWorker,
            passo == ProvisaoV1.PassoPlano ? $"plano {recurso.NomeDoPlano} criado" : $"{passo} criado", agora, ct);
        return true;
    }

    public async Task<bool> FalharAsync(long id, string trabalhador, string mensagem, bool transitoria, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        var agora = relogio.GetUtcNow().UtcDateTime;
        var recurso = await DoTrabalhadorAsync(id, trabalhador, ct);
        if (recurso is null)
            return false;

        var erro = RegrasDaProvisao.Erro(mensagem);
        recurso.Erro = erro;
        string evento;
        string detalhe;
        if (RegrasDaProvisao.TentaDeNovo(transitoria, recurso.Tentativas, cfg))
        {
            var espera = RegrasDaProvisao.Espera(recurso.Tentativas, cfg);
            recurso.Estado = ProvisaoV1.EstadoPedido;
            recurso.ProximaTentativaEmUtc = agora.Add(espera);
            evento = ProvisaoV1.EventoFalhaTransitoria;
            detalhe = $"{erro} — nova tentativa em {(int)espera.TotalSeconds} s";
        }
        else
        {
            recurso.Estado = ProvisaoV1.EstadoFalhou;
            evento = ProvisaoV1.EventoFalha;
            detalhe = transitoria ? $"{erro} — {recurso.Tentativas} tentativas esgotadas" : erro;
        }

        if (!await LiberarAsync(recurso, agora, ct))
            return false;
        await AuditarAsync(db, recurso.Id, recurso.UsuarioId, evento, ProvisaoV1.AutorWorker, RegrasDaProvisao.Erro(detalhe), agora, ct);
        return true;
    }

    private async Task DesistirAsync(RecursoProvisionado recurso, DateTime agora, CancellationToken ct)
    {
        recurso.Estado = ProvisaoV1.EstadoFalhou;
        recurso.Erro = $"A {Provisao.RotuloDaOperacao(recurso.Operacao)} não terminou em {recurso.Tentativas} tentativas: o worker parou no meio de cada uma.";
        if (await LiberarAsync(recurso, agora, ct))
            await AuditarAsync(db, recurso.Id, recurso.UsuarioId, ProvisaoV1.EventoFalha, ProvisaoV1.AutorWorker, recurso.Erro, agora, ct);
    }

    /// <summary>O recurso ainda reservado a este worker (em provisão); nulo quando a reserva passou a outro.</summary>
    private Task<RecursoProvisionado?> DoTrabalhadorAsync(long id, string trabalhador, CancellationToken ct)
        => db.RecursosProvisionados.FirstOrDefaultAsync(r => r.Id == id && r.Estado == ProvisaoV1.EstadoEmProvisao && r.ReservadoPor == trabalhador, ct);

    private async Task<bool> LiberarAsync(RecursoProvisionado recurso, DateTime agora, CancellationToken ct)
    {
        recurso.ReservadoPor = string.Empty;
        recurso.ReservadoAteUtc = null;
        recurso.Versao++;
        recurso.AtualizadoEmUtc = agora;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Só este: os outros candidatos da reserva continuam rastreados.
            db.Entry(recurso).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>Uma linha na auditoria da provisão (só acrescenta).</summary>
    internal static async Task AuditarAsync(DevKitPageDb db, long? recursoId, int usuarioId, string evento, string autor, string detalhe, DateTime agora, CancellationToken ct)
    {
        db.EventosDaProvisao.Add(new EventoDaProvisao
        {
            RecursoId = recursoId,
            UsuarioId = usuarioId,
            Evento = evento,
            Autor = autor.Length <= ValidadorDeEmpresa.TamanhoMaximoDoLogin ? autor : autor[..ValidadorDeEmpresa.TamanhoMaximoDoLogin],
            Detalhe = RegrasDaProvisao.Erro(detalhe),
            EmUtc = agora,
        });
        await db.SaveChangesAsync(ct);
    }
}
