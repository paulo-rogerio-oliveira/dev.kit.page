using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A provisão do lado da API (projeto controle-remoto, item 4): grava o pedido e lê o estado, sem tocar no Azure. Toda consulta
/// parte do usuário do token; a liberação é conferida AQUI, num ponto só, para a rota não ter como esquecê-la.
/// </summary>
public sealed class Provisao(DevKitPageDb db, IOptions<OpcoesDaProvisao> opcoes, TimeProvider relogio) : IProvisao
{
    /// <summary>Quantos recursos a situação lista (os removidos ficam para a auditoria, mas não enchem a tela).</summary>
    public const int RecursosNaSituacao = 20;

    private const string NaoLiberada = "A provisão não está liberada para o seu usuário: peça a liberação ao administrador do dev.kit.";
    private const string NaoEncontrado = "Recurso não encontrado.";

    public async Task<SituacaoDaProvisaoV1> SituacaoAsync(int usuario, CancellationToken ct)
    {
        var origem = await OrigemAsync(db, opcoes.Value, usuario, ct);
        var recursos = await db.RecursosNaNuvem.AsNoTracking().Where(r => r.UsuarioId == usuario)
            .OrderByDescending(r => r.Id).Take(RecursosNaSituacao).ToListAsync(ct);
        return new SituacaoDaProvisaoV1(origem is not null, origem, opcoes.Value.RegioesAceitas(), opcoes.Value.RegiaoSugerida(), recursos.Select(r => Leitura(r)).ToArray());
    }

    public async Task<ResultadoDaGestao<PedidoAceito>> PedirFilaAsync(int usuario, PedidoDeFilaV1 pedido, CancellationToken ct)
    {
        if (await OrigemAsync(db, opcoes.Value, usuario, ct) is null)
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Proibida, NaoLiberada);
        // A liberação antes do formato: sem ela, a resposta é sempre 403, qualquer que seja o corpo.
        if (RegrasDaProvisao.Validar(pedido, opcoes.Value.RegioesAceitas()) is { } invalido)
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Recusada, invalido);

        var chave = (pedido.ChaveDoCliente ?? string.Empty).Trim();
        if (chave.Length > 0 && await db.RecursosNaNuvem.AsNoTracking().FirstOrDefaultAsync(r => r.UsuarioId == usuario && r.ChaveDoCliente == chave, ct) is { } mesmo)
            return new(new PedidoAceito(Leitura(mesmo), false));

        if (await AtivaAsync(usuario, ct) is { } ativa)
            return Existente(ativa);

        var o = opcoes.Value;
        var agora = relogio.GetUtcNow().UtcDateTime;
        var geracao = (await db.RecursosNaNuvem.Where(r => r.UsuarioId == usuario && r.Tipo == ProvisaoV1.TipoFila).MaxAsync(r => (int?)r.Geracao, ct) ?? 0) + 1;
        var regiao = RegrasDaProvisao.NormalizarRegiao(pedido.Regiao);
        var recurso = new RecursoNaNuvem
        {
            UsuarioId = usuario, Tipo = ProvisaoV1.TipoFila, Geracao = geracao, Operacao = ProvisaoV1.OperacaoCriar, Estado = ProvisaoV1.Pedido,
            Regiao = regiao, GrupoDeRecursos = RegrasDaProvisao.GrupoDeRecursos(o.PrefixoDoGrupo, usuario),
            Conta = RegrasDaProvisao.NomeDaConta(o.Ambiente, usuario, geracao),
            Fila = RegrasDaProvisao.NomeDaFilaValido(o.NomeDaFila) ? o.NomeDaFila : "comandos",
            ProximaTentativaEmUtc = agora, PedidoEmUtc = agora, AtualizadoEmUtc = agora, ChaveDoCliente = chave,
        };
        recurso.Eventos.Add(Evento(agora, ProvisaoV1.OperacaoCriar, ProvisaoV1.Pedido, ProvisaoV1.AutorUsuario, $"Fila pedida em {regiao}."));
        db.RecursosNaNuvem.Add(recurso);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois pedidos ao mesmo tempo: o índice único da geração decide, e o perdedor devolve a fila do vencedor.
            db.ChangeTracker.Clear();
            return await AtivaAsync(usuario, ct) is { } vencedora ? Existente(vencedora) : throw new InvalidOperationException("A fila não foi gravada nem por este pedido nem por outro.");
        }

        return new(new PedidoAceito(Leitura(recurso), true));
    }

    public async Task<RecursoNaNuvemV1?> RecursoAsync(int usuario, long id, CancellationToken ct)
    {
        var recurso = await db.RecursosNaNuvem.AsNoTracking().Include(r => r.Eventos)
            .FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuario, ct);
        return recurso is null ? null : Leitura(recurso, comHistorico: true);
    }

    public async Task<ResultadoDaGestao<RecursoNaNuvemV1>> RepetirAsync(int usuario, long id, CancellationToken ct)
    {
        var recurso = await db.RecursosNaNuvem.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuario, ct);
        if (recurso is null)
            return ResultadoDaGestao<RecursoNaNuvemV1>.Falhou(FalhaNaGestao.NaoEncontrado, NaoEncontrado);
        if (RegrasDaProvisao.EmAndamento(recurso.Estado))
            return new(Leitura(recurso)); // o repetir em dobro: já está andando
        if (!RegrasDaProvisao.PodeRepetir(recurso.Estado))
            return ResultadoDaGestao<RecursoNaNuvemV1>.Falhou(FalhaNaGestao.Conflito, $"Só o que falhou se repete: este recurso está {recurso.Estado}.");
        if (recurso.Operacao == ProvisaoV1.OperacaoCriar && await OrigemAsync(db, opcoes.Value, usuario, ct) is null)
            return ResultadoDaGestao<RecursoNaNuvemV1>.Falhou(FalhaNaGestao.Proibida, NaoLiberada);

        var mensagem = recurso.Operacao == ProvisaoV1.OperacaoRemover ? "Remoção repetida pelo usuário." : "Criação repetida pelo usuário.";
        return await RecomecarAsync(recurso, ProvisaoV1.Falhou, recurso.Operacao, mensagem, ct);
    }

    public async Task<ResultadoDaGestao<RecursoNaNuvemV1>> RemoverAsync(int usuario, long id, CancellationToken ct)
    {
        var recurso = await db.RecursosNaNuvem.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuario, ct);
        if (recurso is null)
            return ResultadoDaGestao<RecursoNaNuvemV1>.Falhou(FalhaNaGestao.NaoEncontrado, NaoEncontrado);
        if (recurso.Estado == ProvisaoV1.Removido || (recurso.Operacao == ProvisaoV1.OperacaoRemover && RegrasDaProvisao.EmAndamento(recurso.Estado)))
            return new(Leitura(recurso)); // já removido, ou a remoção pedida de novo
        if (RegrasDaProvisao.PorQueNaoRemove(recurso.Operacao, recurso.Estado) is { } motivo)
            return ResultadoDaGestao<RecursoNaNuvemV1>.Falhou(FalhaNaGestao.Conflito, motivo);

        return await RecomecarAsync(recurso, recurso.Estado, ProvisaoV1.OperacaoRemover, "Remoção pedida pelo usuário.", ct);
    }

    /// <summary>
    /// Devolve o recurso à fila do trabalhador, com a operação e as tentativas zeradas — condicionado ao estado lido, para dois
    /// cliques (ou o trabalhador) não se pisarem: quem chega depois relê o que ficou.
    /// </summary>
    private async Task<ResultadoDaGestao<RecursoNaNuvemV1>> RecomecarAsync(RecursoNaNuvem recurso, string estadoLido, string operacao, string mensagem, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var alterados = await db.RecursosNaNuvem.Where(r => r.Id == recurso.Id && r.Estado == estadoLido && r.Operacao == recurso.Operacao)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Operacao, operacao)
                .SetProperty(r => r.Estado, ProvisaoV1.Pedido)
                .SetProperty(r => r.Tentativas, 0)
                .SetProperty(r => r.UltimoErro, string.Empty)
                .SetProperty(r => r.ProximaTentativaEmUtc, agora)
                .SetProperty(r => r.ConcessaoAteUtc, (DateTime?)null)
                .SetProperty(r => r.Trabalhador, string.Empty)
                .SetProperty(r => r.AtualizadoEmUtc, agora), ct);
        if (alterados > 0)
        {
            db.EventosDaProvisao.Add(Evento(agora, operacao, ProvisaoV1.Pedido, ProvisaoV1.AutorUsuario, mensagem, recurso.Id));
            await db.SaveChangesAsync(ct);
        }

        var atual = await db.RecursosNaNuvem.AsNoTracking().FirstAsync(r => r.Id == recurso.Id, ct);
        return alterados > 0 || (atual.Operacao == operacao && RegrasDaProvisao.EmAndamento(atual.Estado))
            ? new(Leitura(atual))
            : ResultadoDaGestao<RecursoNaNuvemV1>.Falhou(FalhaNaGestao.Conflito, $"O recurso mudou para {atual.Estado} enquanto o pedido chegava: leia de novo.");
    }

    /// <summary>A fila que ainda conta para o usuário (qualquer estado menos removido), a mais nova.</summary>
    private Task<RecursoNaNuvem?> AtivaAsync(int usuario, CancellationToken ct)
        => db.RecursosNaNuvem.AsNoTracking()
            .Where(r => r.UsuarioId == usuario && r.Tipo == ProvisaoV1.TipoFila && r.Estado != ProvisaoV1.Removido)
            .OrderByDescending(r => r.Geracao).FirstOrDefaultAsync(ct);

    /// <summary>O pedido de uma fila quando já há uma: a mesma é devolvida, menos a que está sendo (ou falhou ao ser) removida.</summary>
    private static ResultadoDaGestao<PedidoAceito> Existente(RecursoNaNuvem ativa)
        => ativa.Operacao == ProvisaoV1.OperacaoRemover
            ? ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito, ativa.Estado == ProvisaoV1.Falhou
                ? "A remoção da fila anterior falhou: repita a remoção antes de pedir outra."
                : "A fila anterior ainda está sendo removida: peça a nova quando a remoção terminar.")
            : new(new PedidoAceito(Leitura(ativa), false));

    /// <summary>
    /// De onde vem a liberação do usuário, ou nula: a do admin, ou a do plano da empresa dele com a assinatura ativa. É a mesma
    /// conta que a tela usa para mostrar a ação, então a ação escondida e a recusa da API nunca divergem.
    /// </summary>
    internal static async Task<string?> OrigemAsync(DevKitPageDb db, OpcoesDaProvisao opcoes, int usuario, CancellationToken ct)
    {
        var dados = await db.Usuarios.AsNoTracking().Where(u => u.Id == usuario)
            .Select(u => new
            {
                Plano = u.Empresa != null && u.Empresa.AssinaturaCanceladaEmUtc == null ? u.Empresa.Plano : null,
                Liberada = db.LiberacoesDeProvisao.Any(l => l.UsuarioId == u.Id && l.Liberada),
            })
            .FirstOrDefaultAsync(ct);
        return dados is null ? null : RegrasDaProvisao.Origem(dados.Liberada, dados.Plano, opcoes.PlanosQueLiberam);
    }

    internal static EventoDaProvisao Evento(DateTime em, string operacao, string estado, string autor, string mensagem, long recurso = 0)
        => new() { RecursoId = recurso, EmUtc = em, Operacao = operacao, Estado = estado, Autor = autor, Mensagem = RegrasDaProvisao.MensagemSegura(mensagem) };

    internal static RecursoNaNuvemV1 Leitura(RecursoNaNuvem r, bool comHistorico = false) => new(
        r.Id, r.Tipo, r.Operacao, r.Estado, r.Regiao, r.GrupoDeRecursos, r.Conta, r.Fila, r.Tentativas, r.UltimoErro,
        DevKitPageDb.Utc(r.PedidoEmUtc), DevKitPageDb.Utc(r.AtualizadoEmUtc), r.ProntoEmUtc is { } pronto ? DevKitPageDb.Utc(pronto) : null,
        comHistorico
            ? r.Eventos.OrderBy(e => e.EmUtc).ThenBy(e => e.Id).Select(e => new EventoDaProvisaoV1(DevKitPageDb.Utc(e.EmUtc), e.Operacao, e.Estado, e.Autor, e.Mensagem)).ToArray()
            : null);
}

/// <summary>A liberação da provisão pelo admin: uma linha por usuário, que revogar desliga em vez de apagar.</summary>
public sealed class LiberacoesDaProvisao(DevKitPageDb db, TimeProvider relogio) : ILiberacoesDaProvisao
{
    public async Task<IReadOnlyList<LiberacaoDaProvisaoV1>> ListarAsync(CancellationToken ct)
    {
        var linhas = await db.LiberacoesDeProvisao.AsNoTracking().OrderBy(l => l.UsuarioId)
            .Select(l => new { Liberacao = l, Login = l.Usuario!.Login }).ToListAsync(ct);
        return linhas.Select(l => Leitura(l.Liberacao, l.Login)).ToArray();
    }

    public async Task<ResultadoDaGestao<LiberacaoDaProvisaoV1>> DefinirAsync(int usuario, bool liberada, string autor, CancellationToken ct)
    {
        var login = await db.Usuarios.Where(u => u.Id == usuario).Select(u => u.Login).FirstOrDefaultAsync(ct);
        if (login is null)
            return ResultadoDaGestao<LiberacaoDaProvisaoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Usuário não encontrado.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        var linha = await db.LiberacoesDeProvisao.FirstOrDefaultAsync(l => l.UsuarioId == usuario, ct);
        if (linha is null)
        {
            linha = new LiberacaoDeProvisao { UsuarioId = usuario };
            db.LiberacoesDeProvisao.Add(linha);
        }

        linha.Liberada = liberada;
        linha.AlteradaPor = autor.Length <= 100 ? autor : autor[..100];
        linha.AlteradaEmUtc = agora;
        await db.SaveChangesAsync(ct);
        return new(Leitura(linha, login));
    }

    private static LiberacaoDaProvisaoV1 Leitura(LiberacaoDeProvisao l, string login)
        => new(l.UsuarioId, login, l.Liberada, l.AlteradaPor, DevKitPageDb.Utc(l.AlteradaEmUtc));
}

/// <summary>
/// O motor da provisão, que roda no TRABALHADOR (o processo <c>DevKitPage.Provisao</c>), nunca na API. Pega um pedido vencido
/// com uma concessão gravada no SQL (o <c>ExecuteUpdate</c> com a condição decide entre dois trabalhadores), executa no
/// <see cref="IProvedorDeNuvem"/> e grava o desfecho só se ainda tiver a concessão: o trabalhador que travou e perdeu a vez não
/// sobrescreve o que o outro fez. A concessão vencida volta para a fila contando uma falha, então a queda no meio não trava
/// o pedido nem o repete para sempre.
/// </summary>
public sealed class MotorDeProvisao(
    DevKitPageDb db, IProvedorDeNuvem nuvem, IOptions<OpcoesDaProvisao> opcoes, TimeProvider relogio, ILogger<MotorDeProvisao> log) : IMotorDeProvisao
{
    /// <summary>Quantos candidatos o motor olha por volta: se outro trabalhador pegar o primeiro, ele tenta o seguinte.</summary>
    private const int Candidatos = 5;

    public async Task<bool> ProcessarProximoAsync(string trabalhador, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var candidatos = await Vencidos(agora).OrderBy(r => r.ProximaTentativaEmUtc).ThenBy(r => r.Id)
            .Select(r => new { r.Id, r.Estado, r.Trabalhador }).Take(Candidatos).ToListAsync(ct);

        foreach (var candidato in candidatos)
        {
            var retomada = candidato.Estado == ProvisaoV1.EmProvisao;
            var ate = agora.AddSeconds(Math.Max(30, opcoes.Value.ConcessaoSegundos));
            var pegou = await Vencidos(agora).Where(r => r.Id == candidato.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Estado, ProvisaoV1.EmProvisao)
                    .SetProperty(r => r.ConcessaoAteUtc, ate)
                    .SetProperty(r => r.Trabalhador, trabalhador)
                    .SetProperty(r => r.Tentativas, r => r.Tentativas + (retomada ? 1 : 0))
                    .SetProperty(r => r.AtualizadoEmUtc, agora), ct);
            if (pegou == 0)
                continue; // outro trabalhador pegou antes

            await ExecutarAsync(candidato.Id, trabalhador, retomada ? candidato.Trabalhador : null, ct);
            return true;
        }

        return false;
    }

    /// <summary>O pedido que o trabalhador pode pegar: vencido na fila, ou com a concessão de outro já vencida.</summary>
    private IQueryable<RecursoNaNuvem> Vencidos(DateTime agora)
        => db.RecursosNaNuvem.Where(r =>
            (r.Estado == ProvisaoV1.Pedido && r.ProximaTentativaEmUtc <= agora) ||
            (r.Estado == ProvisaoV1.EmProvisao && r.ConcessaoAteUtc != null && r.ConcessaoAteUtc < agora));

    private async Task ExecutarAsync(long id, string trabalhador, string? anterior, CancellationToken ct)
    {
        var o = opcoes.Value;
        var recurso = await db.RecursosNaNuvem.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        var autor = $"{ProvisaoV1.AutorTrabalhador}:{trabalhador}";

        if (anterior is not null)
        {
            var queda = $"A concessão do trabalhador {anterior} venceu no meio da operação (falha {recurso.Tentativas} de {o.MaximoDeTentativas}).";
            if (recurso.Tentativas >= o.MaximoDeTentativas)
            {
                await GravarAsync(recurso, trabalhador, autor, ProvisaoV1.Falhou, recurso.Tentativas, queda, queda, ct);
                return;
            }

            await RegistrarAsync(recurso, autor, ProvisaoV1.EmProvisao, $"{queda} Retomada.", ct);
        }

        var alvo = new AlvoNaNuvem(recurso.GrupoDeRecursos, recurso.Conta, recurso.Fila, recurso.Regiao, RegrasDaProvisao.Etiquetas(o.Ambiente, recurso.UsuarioId, recurso.Id));
        ResultadoNaNuvem resultado;
        try
        {
            resultado = recurso.Operacao == ProvisaoV1.OperacaoRemover
                ? await nuvem.RemoverFilaAsync(alvo, ct)
                : await nuvem.GarantirFilaAsync(alvo, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // O trabalhador está parando: devolve o pedido em vez de deixar a concessão vencer.
            await DevolverAsync(recurso.Id, trabalhador);
            throw;
        }
        catch (Exception ex)
        {
            // O tipo, e não a mensagem: ela pode trazer o que não deve ir para a auditoria. O log do trabalhador tem o resto.
            log.LogWarning(ex, "Provisão do recurso {Recurso}: falha inesperada no provedor.", recurso.Id);
            resultado = new ResultadoNaNuvem(SituacaoNaNuvem.FalhaTransitoria, $"Falha inesperada no trabalhador ({ex.GetType().Name}).");
        }

        log.LogInformation("Provisão do recurso {Recurso} ({Operacao}): {Situacao}.", recurso.Id, recurso.Operacao, resultado.Situacao);
        await DesfechoAsync(recurso, trabalhador, autor, resultado, ct);
    }

    private Task DesfechoAsync(RecursoNaNuvem recurso, string trabalhador, string autor, ResultadoNaNuvem resultado, CancellationToken ct)
    {
        var o = opcoes.Value;
        var criar = recurso.Operacao != ProvisaoV1.OperacaoRemover;
        var motivo = RegrasDaProvisao.MensagemSegura(resultado.Mensagem);
        switch (resultado.Situacao)
        {
            case SituacaoNaNuvem.Pronto:
                return GravarAsync(recurso, trabalhador, autor, criar ? ProvisaoV1.Pronto : ProvisaoV1.Removido, 0, string.Empty,
                    criar ? "Fila pronta no Azure." : "Fila removida do Azure.", ct);

            case SituacaoNaNuvem.EmAndamento:
                // O Azure ainda está fazendo: volta em pouco tempo, sem contar falha nem encher a auditoria a cada volta.
                return GravarAsync(recurso, trabalhador, autor, ProvisaoV1.Pedido, recurso.Tentativas, recurso.UltimoErro, null, ct,
                    TimeSpan.FromSeconds(Math.Max(1, o.AcompanhamentoSegundos)));

            case SituacaoNaNuvem.FalhaDefinitiva:
                return GravarAsync(recurso, trabalhador, autor, ProvisaoV1.Falhou, recurso.Tentativas + 1, motivo, $"Falha definitiva, sem nova tentativa: {motivo}", ct);

            default:
                var falhas = recurso.Tentativas + 1;
                if (falhas >= o.MaximoDeTentativas)
                    return GravarAsync(recurso, trabalhador, autor, ProvisaoV1.Falhou, falhas, motivo, $"Falhou depois de {falhas} tentativas: {motivo}", ct);
                var espera = RegrasDaProvisao.Espera(falhas, o.EsperaInicialSegundos, o.EsperaMaximaSegundos);
                return GravarAsync(recurso, trabalhador, autor, ProvisaoV1.Pedido, falhas, motivo,
                    $"Tentativa {falhas} de {o.MaximoDeTentativas} falhou; a próxima em {(int)espera.TotalSeconds}s: {motivo}", ct, espera);
        }
    }

    /// <summary>
    /// Grava o desfecho SÓ se este trabalhador ainda tem a concessão, e o evento da auditoria junto. Sem a concessão, outro
    /// já retomou: o desfecho dele é o que vale.
    /// </summary>
    private async Task GravarAsync(
        RecursoNaNuvem recurso, string trabalhador, string autor, string estado, int tentativas, string erro, string? evento, CancellationToken ct,
        TimeSpan? espera = null)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var proxima = agora + (espera ?? TimeSpan.Zero);
        var pronto = estado == ProvisaoV1.Pronto ? agora : recurso.ProntoEmUtc;
        var gravados = await db.RecursosNaNuvem.Where(r => r.Id == recurso.Id && r.Estado == ProvisaoV1.EmProvisao && r.Trabalhador == trabalhador)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Estado, estado)
                .SetProperty(r => r.Tentativas, tentativas)
                .SetProperty(r => r.UltimoErro, erro)
                .SetProperty(r => r.ProximaTentativaEmUtc, proxima)
                .SetProperty(r => r.ProntoEmUtc, pronto)
                .SetProperty(r => r.ConcessaoAteUtc, (DateTime?)null)
                .SetProperty(r => r.Trabalhador, string.Empty)
                .SetProperty(r => r.AtualizadoEmUtc, agora), CancellationToken.None);
        if (gravados == 0)
        {
            log.LogWarning("Provisão do recurso {Recurso}: a concessão de {Trabalhador} venceu antes do desfecho; vale o de quem retomou.", recurso.Id, trabalhador);
            return;
        }

        if (evento is not null)
            await RegistrarAsync(recurso, autor, estado, evento, ct);
    }

    private async Task RegistrarAsync(RecursoNaNuvem recurso, string autor, string estado, string mensagem, CancellationToken ct)
    {
        db.EventosDaProvisao.Add(Provisao.Evento(relogio.GetUtcNow().UtcDateTime, recurso.Operacao, estado, autor, mensagem, recurso.Id));
        await db.SaveChangesAsync(ct);
    }

    private async Task DevolverAsync(long id, string trabalhador)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        await db.RecursosNaNuvem.Where(r => r.Id == id && r.Estado == ProvisaoV1.EmProvisao && r.Trabalhador == trabalhador)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Estado, ProvisaoV1.Pedido)
                .SetProperty(r => r.ProximaTentativaEmUtc, agora)
                .SetProperty(r => r.ConcessaoAteUtc, (DateTime?)null)
                .SetProperty(r => r.Trabalhador, string.Empty)
                .SetProperty(r => r.AtualizadoEmUtc, agora), CancellationToken.None);
    }
}

/// <summary>O registro do motor, SÓ no trabalhador: a API não o recebe, e o provedor de nuvem vem do projeto dele.</summary>
public static class ComposicaoDoMotor
{
    public static IServiceCollection AdicionarMotorDeProvisao(this IServiceCollection servicos)
    {
        servicos.AddScoped<IMotorDeProvisao, MotorDeProvisao>();
        return servicos;
    }
}
