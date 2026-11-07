using System.Collections.Concurrent;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A conversa no banco: o comando (com o estado), a resposta e o evento do SSE — sempre filtrando pelo usuário do token. Toda
/// mudança de estado é uma gravação CONDICIONAL (<c>WHERE Estado = o lido</c>): o recebimento repetido que chega junto, e o
/// TTL que vence no mesmo instante, têm um vencedor só — o comando nunca é executado duas vezes nem entregue depois de expirar.
/// </summary>
public sealed class ConversaRemota(
    DevKitPageDb db, IFilaDeComandos fila, IAvisoDaConversa aviso, TimeProvider relogio, IOptions<OpcoesDoRemoto> opcoes) : IConversaRemota
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoDaConversa<ComandoAceito>> EnviarAsync(int usuarioId, ComandoNovoV1 pedido, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        await ExpirarVencidosAsync(usuarioId, ct);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var id = string.IsNullOrEmpty(pedido.Id) ? Guid.NewGuid().ToString("N") : pedido.Id;
        var tipo = RegrasDoRemoto.Normalizar(pedido.Tipo);
        var texto = (pedido.Texto ?? string.Empty).Trim();

        var existente = await db.ComandosRemotos.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == usuarioId && c.ComandoId == id, ct);
        if (existente is not null)
            return await ReenviarAsync(existente, pedido.MaquinaId, tipo, texto, pedido.ConfirmarReinicio, ct);

        var maquina = await db.MaquinasRemotas.AsNoTracking().FirstOrDefaultAsync(m => m.UsuarioId == usuarioId && m.MaquinaId == pedido.MaquinaId, ct);
        if (maquina is null)
            return ResultadoDaConversa<ComandoAceito>.Falhou(FalhaNaConversa.NaoEncontrado, "Máquina não encontrada, ou não é sua.");
        if (tipo == ConversaV1.ComandoReiniciar && maquina.TurnoEmAndamento && !pedido.ConfirmarReinicio)
            return ResultadoDaConversa<ComandoAceito>.Falhou(FalhaNaConversa.TurnoEmAndamento,
                "Há um turno do agente em andamento nesta máquina: reiniciar o interrompe. Confirme para reiniciar assim mesmo.");
        if (await FilaProntaAsync(usuarioId, ct) is not { } filaDoUsuario)
            return ResultadoDaConversa<ComandoAceito>.Falhou(FalhaNaConversa.SemFila,
                "Você ainda não tem a fila do controle remoto pronta. Peça-a na sua conta (Fila do controle remoto).");

        // O handshake (o pedido do catálogo) ainda na fila serve a quem pede de novo: o site que reabre o Board e o duplo
        // "Atualizar" não empilham pedidos que o dev.kit responderia todos com o mesmo catálogo.
        if (tipo == ConversaV1.ComandoCatalogo && await db.ComandosRemotos.AsNoTracking()
                .Where(c => c.UsuarioId == usuarioId && c.MaquinaId == pedido.MaquinaId && c.Tipo == ConversaV1.ComandoCatalogo
                            && (c.Estado == ConversaV1.EstadoPendente || c.Estado == ConversaV1.EstadoNaFila) && c.ExpiraEmUtc > agora)
                .OrderByDescending(c => c.CriadoEmUtc).FirstOrDefaultAsync(ct) is { } aguardando)
            return new(new ComandoAceito(Leitura(aguardando), Novo: false));

        // O limite por hora é de quem pode abrir turno do agente: o handshake não abre, e não conta nem é barrado por ele.
        var inicioDaJanela = agora.AddHours(-1);
        var naJanela = await db.ComandosRemotos.AsNoTracking()
            .Where(c => c.UsuarioId == usuarioId && c.CriadoEmUtc > inicioDaJanela && c.Tipo != ConversaV1.ComandoCatalogo)
            .Select(c => c.CriadoEmUtc).ToListAsync(ct);
        if (tipo != ConversaV1.ComandoCatalogo && RegrasDaConversa.LimiteCheio(naJanela.Count, cfg))
        {
            var espera = RegrasDaConversa.TenteEm(naJanela.Min(), agora);
            return ResultadoDaConversa<ComandoAceito>.Falhou(FalhaNaConversa.Limite,
                $"Você mandou {naJanela.Count} comandos na última hora (o limite é {Math.Max(1, cfg.ComandosPorHora)}). Tente de novo em {Math.Ceiling(espera.TotalMinutes)} min.",
                espera);
        }

        var dispositivo = RegrasDoRemoto.Normalizar(pedido.Dispositivo);
        var comando = new ComandoRemoto
        {
            UsuarioId = usuarioId,
            ComandoId = id,
            MaquinaId = pedido.MaquinaId,
            Tipo = tipo,
            Texto = texto,
            Dispositivo = dispositivo.Length == 0 ? ConversaV1.DispositivoNavegador : dispositivo,
            Estado = ConversaV1.EstadoPendente,
            RecursoId = filaDoUsuario.RecursoId,
            CriadoEmUtc = agora,
            ExpiraEmUtc = agora.Add(RegrasDaConversa.Ttl(pedido.TtlSegundos, cfg)),
            AtualizadoEmUtc = agora,
        };
        db.ComandosRemotos.Add(comando);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois envios com o mesmo id ao mesmo tempo: o índice único deixa um; o outro devolve o que ficou.
            db.ChangeTracker.Clear();
            var vencedor = await db.ComandosRemotos.AsNoTracking().FirstAsync(c => c.UsuarioId == usuarioId && c.ComandoId == id, ct);
            return await ReenviarAsync(vencedor, pedido.MaquinaId, tipo, texto, pedido.ConfirmarReinicio, ct);
        }

        return await EnfileirarAsync(comando, filaDoUsuario, novo: true, pedido.ConfirmarReinicio, ct);
    }

    /// <summary>
    /// O reenvio com um id que já existe: o mesmo conteúdo devolve o comando (o que falhou na fila tenta de novo, sem contar no
    /// limite); outro conteúdo é <see cref="FalhaNaConversa.IdEmUso"/>. A confirmação do reinício vem do reenvio, como no envio:
    /// ela não é gravada, só levada na mensagem da fila.
    /// </summary>
    private async Task<ResultadoDaConversa<ComandoAceito>> ReenviarAsync(
        ComandoRemoto existente, string maquinaId, string tipo, string texto, bool confirmarReinicio, CancellationToken ct)
    {
        if (existente.MaquinaId != maquinaId || existente.Tipo != tipo || existente.Texto != texto)
            return ResultadoDaConversa<ComandoAceito>.Falhou(FalhaNaConversa.IdEmUso, "Este id já é de outro comando seu. Mande o comando novo com outro id.");
        if (existente.Estado != ConversaV1.EstadoFalhou)
            return new(new ComandoAceito(Leitura(existente), Novo: false));
        if (await FilaProntaAsync(existente.UsuarioId, ct) is not { } filaDoUsuario)
            return ResultadoDaConversa<ComandoAceito>.Falhou(FalhaNaConversa.SemFila,
                "Você não tem mais a fila do controle remoto pronta. Peça-a na sua conta (Fila do controle remoto).");
        existente.RecursoId = filaDoUsuario.RecursoId;
        return await EnfileirarAsync(existente, filaDoUsuario, novo: false, confirmarReinicio, ct);
    }

    /// <summary>
    /// Põe o comando na fila com o TTL que falta; a fila fora do ar o deixa <c>falhou</c>, com a mensagem. O reinício leva a
    /// confirmação do usuário (<paramref name="confirmarReinicio"/>), que o serviço confere de novo contra o turno na hora.
    /// </summary>
    private async Task<ResultadoDaConversa<ComandoAceito>> EnfileirarAsync(
        ComandoRemoto comando, FilaDoUsuario filaDoUsuario, bool novo, bool confirmarReinicio, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var mensagem = JsonSerializer.Serialize(new MensagemDaFilaV1(RemotoV1.Contrato, comando.ComandoId, comando.MaquinaId, comando.Tipo, comando.Texto,
            ConversaV1.OrigemRemoto, comando.Dispositivo, DevKitPageDb.Utc(comando.CriadoEmUtc), DevKitPageDb.Utc(comando.ExpiraEmUtc),
            comando.Tipo == ConversaV1.ComandoReiniciar && confirmarReinicio), Json);
        string? erro = null;
        try
        {
            await fila.EnviarAsync(filaDoUsuario, mensagem, comando.ExpiraEmUtc - agora, ct);
        }
        catch (FalhaNaFila falha)
        {
            erro = RegrasDaProvisao.Erro(falha.Message);
        }

        var lido = comando.Estado;
        var estado = erro is null ? ConversaV1.EstadoNaFila : ConversaV1.EstadoFalhou;
        var mudou = await db.ComandosRemotos.Where(c => c.Id == comando.Id && c.Estado == lido)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Estado, estado).SetProperty(c => c.Erro, erro ?? string.Empty)
                .SetProperty(c => c.RecursoId, comando.RecursoId).SetProperty(c => c.AtualizadoEmUtc, agora), ct);
        // Perdeu a corrida (o dev.kit já recebeu — a mensagem chegou antes da resposta da fila): vale o que está gravado.
        var atual = await Reler(comando.Id, ct);
        if (mudou == 1)
            await RegistrarComandoAsync(atual, agora, ct);
        var leitura = Leitura(atual);
        return erro is not null && mudou == 1
            ? new(new ComandoAceito(leitura, novo), FalhaNaConversa.FilaIndisponivel, $"A fila não aceitou o comando: {erro} Mande de novo com o mesmo id para tentar outra vez.")
            : new(new ComandoAceito(leitura, novo));
    }

    public async Task<ComandoRemotoV1?> ComandoAsync(int usuarioId, string comandoId, CancellationToken ct)
    {
        await ExpirarVencidosAsync(usuarioId, ct);
        var comando = await db.ComandosRemotos.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == usuarioId && c.ComandoId == comandoId, ct);
        return comando is null ? null : Leitura(comando);
    }

    public async Task<ConversaDaMaquinaV1?> ConversaAsync(int usuarioId, string maquinaId, CancellationToken ct)
    {
        if (!await db.MaquinasRemotas.AsNoTracking().AnyAsync(m => m.UsuarioId == usuarioId && m.MaquinaId == maquinaId, ct))
            return null;
        await ExpirarVencidosAsync(usuarioId, ct);
        // O último evento ANTES do histórico: o que mudar entre as duas leituras chega de novo pelo SSE (o site junta pelo id).
        var ultimo = await db.EventosDaConversa.AsNoTracking().Where(e => e.UsuarioId == usuarioId).MaxAsync(e => (long?)e.Id, ct) ?? 0;
        var comandos = await db.ComandosRemotos.AsNoTracking().Where(c => c.UsuarioId == usuarioId && c.MaquinaId == maquinaId)
            .OrderByDescending(c => c.CriadoEmUtc).ThenByDescending(c => c.Id).Take(ConversaV1.MaximoDoHistorico).ToListAsync(ct);
        var respostas = await db.RespostasRemotas.AsNoTracking().Where(r => r.UsuarioId == usuarioId && r.MaquinaId == maquinaId)
            .OrderByDescending(r => r.RecebidaEmUtc).ThenByDescending(r => r.Id).Take(ConversaV1.MaximoDoHistorico).ToListAsync(ct);
        // As falas na ordem em que foram ditas: a que chega atrasada (o reenvio do dev.kit) entra no lugar dela.
        var falas = await db.FalasRemotas.AsNoTracking().Where(f => f.UsuarioId == usuarioId && f.MaquinaId == maquinaId)
            .OrderByDescending(f => f.DitaEmUtc).ThenByDescending(f => f.Id).Take(ConversaV1.MaximoDoHistorico).ToListAsync(ct);
        return new ConversaDaMaquinaV1(
            comandos.AsEnumerable().Reverse().Select(Leitura).ToArray(),
            respostas.AsEnumerable().Reverse().Select(Leitura).ToArray(),
            ultimo,
            falas.AsEnumerable().Reverse().Select(Leitura).ToArray());
    }

    public async Task<IReadOnlyList<EventoDaConversaV1>> EventosAsync(int usuarioId, string? maquinaId, long depoisDe, int maximo, CancellationToken ct)
    {
        var consulta = db.EventosDaConversa.AsNoTracking().Where(e => e.UsuarioId == usuarioId && e.Id > depoisDe);
        if (maquinaId is not null)
            consulta = consulta.Where(e => e.MaquinaId == maquinaId);
        var linhas = await consulta.OrderBy(e => e.Id).Take(Math.Clamp(maximo, 1, 500)).ToListAsync(ct);
        return linhas.Select(l => JsonSerializer.Deserialize<EventoDaConversaV1>(l.Conteudo, Json)! with { Id = l.Id }).ToArray();
    }

    public async Task<ResultadoDaConversa<SasDaFilaV1>> SasAsync(int usuarioId, string maquinaId, CancellationToken ct)
    {
        if (!await db.MaquinasRemotas.AsNoTracking().AnyAsync(m => m.UsuarioId == usuarioId && m.MaquinaId == maquinaId, ct))
            return ResultadoDaConversa<SasDaFilaV1>.Falhou(FalhaNaConversa.NaoEncontrado,
                "Máquina não encontrada: o serviço manda o heartbeat antes de pedir a SAS.");
        if (await FilaProntaAsync(usuarioId, ct) is not { } filaDoUsuario)
            return ResultadoDaConversa<SasDaFilaV1>.Falhou(FalhaNaConversa.SemFila, "O usuário ainda não tem a fila do controle remoto pronta.");
        var ate = relogio.GetUtcNow().Add(RegrasDaConversa.ValidadeDaSas(opcoes.Value));
        try
        {
            var acesso = await fila.EmitirSasAsync(filaDoUsuario, ate, ct);
            return new(new SasDaFilaV1(acesso.Endpoint, acesso.Sas, ConversaV1.PermissaoDaSas, acesso.ExpiraEm));
        }
        catch (FalhaNaFila falha)
        {
            return ResultadoDaConversa<SasDaFilaV1>.Falhou(FalhaNaConversa.FilaIndisponivel, RegrasDaProvisao.Erro(falha.Message));
        }
    }

    public async Task<ResultadoDaConversa<RecebimentoAceitoV1>> ReceberAsync(int usuarioId, string comandoId, string maquinaId, CancellationToken ct)
    {
        await ExpirarVencidosAsync(usuarioId, ct);
        var comando = await db.ComandosRemotos.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == usuarioId && c.ComandoId == comandoId, ct);
        if (comando is null)
            return ResultadoDaConversa<RecebimentoAceitoV1>.Falhou(FalhaNaConversa.NaoEncontrado, "Comando não encontrado.");
        if (comando.MaquinaId != maquinaId)
            return ResultadoDaConversa<RecebimentoAceitoV1>.Falhou(FalhaNaConversa.OutraMaquina,
                "Este comando é de outra máquina sua: não o execute (e não o apague da fila).");
        if (comando.Estado == ConversaV1.EstadoExpirado)
            return new(new RecebimentoAceitoV1(comandoId, comando.Estado, Executar: false, Repetido: false));
        if (!RegrasDaConversa.AguardaEntrega(comando.Estado))
            return new(new RecebimentoAceitoV1(comandoId, comando.Estado, Executar: false, Repetido: true));

        var agora = relogio.GetUtcNow().UtcDateTime;
        var lido = comando.Estado;
        var venceu = await db.ComandosRemotos.Where(c => c.Id == comando.Id && c.Estado == lido)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Estado, ConversaV1.EstadoEntregue).SetProperty(c => c.EntregueEmUtc, agora)
                .SetProperty(c => c.Erro, string.Empty).SetProperty(c => c.AtualizadoEmUtc, agora), ct) == 1;
        var atual = await Reler(comando.Id, ct);
        if (!venceu)
            // Outro recebimento (a fila entregou duas vezes) ou o TTL chegou antes: não executa.
            return new(new RecebimentoAceitoV1(comandoId, atual.Estado, Executar: false, Repetido: atual.Estado != ConversaV1.EstadoExpirado));
        await RegistrarComandoAsync(atual, agora, ct);
        return new(new RecebimentoAceitoV1(comandoId, atual.Estado, Executar: true, Repetido: false));
    }

    public async Task<ResultadoDaConversa<RespostaRecebidaV1>> ResponderAsync(int usuarioId, RespostaDoDevKitV1 resposta, CancellationToken ct)
    {
        var maquinaId = resposta.MaquinaId;
        if (!await db.MaquinasRemotas.AsNoTracking().AnyAsync(m => m.UsuarioId == usuarioId && m.MaquinaId == maquinaId, ct))
            return ResultadoDaConversa<RespostaRecebidaV1>.Falhou(FalhaNaConversa.NaoEncontrado, "Máquina não encontrada.");
        if (await JaRecebidaAsync(usuarioId, maquinaId, resposta.Id, ct) is { } repetida)
            return new(repetida);

        var comandoId = resposta.ComandoId ?? string.Empty;
        ComandoRemoto? comando = null;
        if (comandoId.Length > 0)
        {
            comando = await db.ComandosRemotos.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == usuarioId && c.ComandoId == comandoId, ct);
            if (comando is null)
                return ResultadoDaConversa<RespostaRecebidaV1>.Falhou(FalhaNaConversa.NaoEncontrado, "Comando não encontrado.");
            if (comando.MaquinaId != maquinaId)
                return ResultadoDaConversa<RespostaRecebidaV1>.Falhou(FalhaNaConversa.OutraMaquina, "Este comando é de outra máquina sua.");
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        var tipo = RegrasDoRemoto.Normalizar(resposta.Tipo);
        var linha = new RespostaRemota
        {
            UsuarioId = usuarioId,
            MaquinaId = maquinaId,
            RespostaId = resposta.Id,
            ComandoId = comandoId,
            Tipo = tipo,
            Texto = (resposta.Texto ?? string.Empty).Trim(),
            GeradaEmUtc = resposta.GeradaEm.UtcDateTime,
            RecebidaEmUtc = agora,
        };
        db.RespostasRemotas.Add(linha);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // O mesmo reenvio do dev.kit em duas chamadas ao mesmo tempo: o índice único deixa uma.
            db.ChangeTracker.Clear();
            return new((await JaRecebidaAsync(usuarioId, maquinaId, resposta.Id, ct))!);
        }
        await RegistrarAsync(usuarioId, maquinaId, ConversaV1.EventoResposta, null, Leitura(linha), agora, ct);

        if (comando is not null)
        {
            var estado = RegrasDaConversa.EstadoDepoisDaResposta(comando.Estado, tipo);
            var lido = comando.Estado;
            if (estado != lido && await db.ComandosRemotos.Where(c => c.Id == comando.Id && c.Estado == lido)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Estado, estado)
                        .SetProperty(c => c.EntregueEmUtc, c => c.EntregueEmUtc ?? agora)
                        .SetProperty(c => c.ConcluidoEmUtc, c => c.ConcluidoEmUtc ?? agora)
                        .SetProperty(c => c.AtualizadoEmUtc, agora), ct) == 1)
                await RegistrarComandoAsync(await Reler(comando.Id, ct), agora, ct);
        }

        return new(new RespostaRecebidaV1(resposta.Id, Repetida: false, DevKitPageDb.Utc(agora)));
    }

    public async Task<ResultadoDaConversa<FalaRecebidaV1>> FalarAsync(int usuarioId, FalaDoDevKitV1 fala, CancellationToken ct)
    {
        var maquinaId = fala.MaquinaId;
        if (!await db.MaquinasRemotas.AsNoTracking().AnyAsync(m => m.UsuarioId == usuarioId && m.MaquinaId == maquinaId, ct))
            return ResultadoDaConversa<FalaRecebidaV1>.Falhou(FalhaNaConversa.NaoEncontrado, "Máquina não encontrada.");

        var linha = new FalaRemota
        {
            UsuarioId = usuarioId,
            MaquinaId = maquinaId,
            FalaId = fala.Id,
            Autor = RegrasDoRemoto.Normalizar(fala.Autor),
            Origem = RegrasDoRemoto.Normalizar(fala.Origem),
            Texto = (fala.Texto ?? string.Empty).Trim(),
            ComandoId = fala.ComandoId ?? string.Empty,
            DitaEmUtc = fala.DitaEm.UtcDateTime,
        };
        if (await FalaGravadaAsync(linha, ct) is { } gravada)
            return gravada;

        if (linha.ComandoId.Length > 0)
        {
            var comando = await db.ComandosRemotos.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == usuarioId && c.ComandoId == linha.ComandoId, ct);
            if (comando is null)
                return ResultadoDaConversa<FalaRecebidaV1>.Falhou(FalhaNaConversa.NaoEncontrado, "Comando não encontrado.");
            if (comando.MaquinaId != maquinaId)
                return ResultadoDaConversa<FalaRecebidaV1>.Falhou(FalhaNaConversa.OutraMaquina, "Este comando é de outra máquina sua.");
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        linha.RecebidaEmUtc = agora;
        db.FalasRemotas.Add(linha);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // O mesmo reenvio do dev.kit em duas chamadas ao mesmo tempo: o índice único deixa uma.
            db.ChangeTracker.Clear();
            return (await FalaGravadaAsync(linha, ct))!;
        }
        await RegistrarAsync(usuarioId, maquinaId, ConversaV1.EventoFala, null, null, agora, ct, Leitura(linha));
        return new(new FalaRecebidaV1(fala.Id, Repetida: false, DevKitPageDb.Utc(agora)));
    }

    /// <summary>
    /// A fala que já tem o id da <paramref name="nova"/>: a mesma é o reenvio (<see cref="FalaRecebidaV1.Repetida"/>), outra é
    /// <see cref="FalhaNaConversa.IdEmUso"/>; nula quando o id é novo.
    /// </summary>
    private async Task<ResultadoDaConversa<FalaRecebidaV1>?> FalaGravadaAsync(FalaRemota nova, CancellationToken ct)
    {
        var gravada = await db.FalasRemotas.AsNoTracking()
            .FirstOrDefaultAsync(f => f.UsuarioId == nova.UsuarioId && f.MaquinaId == nova.MaquinaId && f.FalaId == nova.FalaId, ct);
        if (gravada is null)
            return null;
        if (gravada.Autor != nova.Autor || gravada.Origem != nova.Origem || gravada.Texto != nova.Texto || gravada.ComandoId != nova.ComandoId)
            return ResultadoDaConversa<FalaRecebidaV1>.Falhou(FalhaNaConversa.IdEmUso, "Este id já é de outra fala desta máquina. Mande a fala nova com outro id.");
        return new(new FalaRecebidaV1(gravada.FalaId, Repetida: true, DevKitPageDb.Utc(gravada.RecebidaEmUtc)));
    }

    public async Task<int> ExpirarVencidosAsync(int? usuarioId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var consulta = db.ComandosRemotos.AsNoTracking()
            .Where(c => (c.Estado == ConversaV1.EstadoPendente || c.Estado == ConversaV1.EstadoNaFila || c.Estado == ConversaV1.EstadoFalhou) && c.ExpiraEmUtc <= agora);
        if (usuarioId is { } usuario)
            consulta = consulta.Where(c => c.UsuarioId == usuario);
        var vencidos = await consulta.OrderBy(c => c.ExpiraEmUtc).Take(500).ToListAsync(ct);

        var expirados = 0;
        foreach (var comando in vencidos)
        {
            var lido = comando.Estado;
            if (await db.ComandosRemotos.Where(c => c.Id == comando.Id && c.Estado == lido)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Estado, ConversaV1.EstadoExpirado).SetProperty(c => c.AtualizadoEmUtc, agora), ct) != 1)
                continue;
            comando.Estado = ConversaV1.EstadoExpirado;
            comando.AtualizadoEmUtc = agora;
            await RegistrarComandoAsync(comando, agora, ct);
            expirados++;
        }
        return expirados;
    }

    private async Task<RespostaRecebidaV1?> JaRecebidaAsync(int usuarioId, string maquinaId, string respostaId, CancellationToken ct)
    {
        var gravada = await db.RespostasRemotas.AsNoTracking()
            .FirstOrDefaultAsync(r => r.UsuarioId == usuarioId && r.MaquinaId == maquinaId && r.RespostaId == respostaId, ct);
        return gravada is null ? null : new RespostaRecebidaV1(respostaId, Repetida: true, DevKitPageDb.Utc(gravada.RecebidaEmUtc));
    }

    /// <summary>A fila PRONTA do usuário (a mais recente); nula sem nenhuma — a que está sendo criada ou removida não serve.</summary>
    private async Task<FilaDoUsuario?> FilaProntaAsync(int usuarioId, CancellationToken ct)
    {
        var recurso = await db.RecursosProvisionados.AsNoTracking()
            .Where(r => r.UsuarioId == usuarioId && r.Tipo == ProvisaoV1.TipoFila && r.Estado == ProvisaoV1.EstadoPronto)
            .OrderByDescending(r => r.ProntoEmUtc).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        return recurso is null ? null : new FilaDoUsuario(recurso.Id, usuarioId, recurso.Nome, recurso.Fila, recurso.IdNoAzure, recurso.Endpoint);
    }

    private Task<ComandoRemoto> Reler(long id, CancellationToken ct) => db.ComandosRemotos.AsNoTracking().FirstAsync(c => c.Id == id, ct);

    private Task RegistrarComandoAsync(ComandoRemoto comando, DateTime agora, CancellationToken ct)
        => RegistrarAsync(comando.UsuarioId, comando.MaquinaId, ConversaV1.EventoComando, Leitura(comando), null, agora, ct);

    /// <summary>Acrescenta o evento do SSE e acorda quem o espera nesta instância.</summary>
    private async Task RegistrarAsync(int usuarioId, string maquinaId, string tipo, ComandoRemotoV1? comando, RespostaRemotaV1? resposta, DateTime agora,
        CancellationToken ct, FalaRemotaV1? fala = null)
    {
        db.EventosDaConversa.Add(new EventoDaConversa
        {
            UsuarioId = usuarioId,
            MaquinaId = maquinaId,
            Tipo = tipo,
            Conteudo = JsonSerializer.Serialize(new EventoDaConversaV1(0, tipo, maquinaId, comando, resposta, DevKitPageDb.Utc(agora), fala), Json),
            EmUtc = agora,
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        aviso.Avisar(usuarioId);
    }

    internal static ComandoRemotoV1 Leitura(ComandoRemoto c)
        => new(c.ComandoId, c.MaquinaId, c.Tipo, c.Texto, c.Dispositivo, c.Estado, DevKitPageDb.Utc(c.CriadoEmUtc), DevKitPageDb.Utc(c.ExpiraEmUtc),
            c.EntregueEmUtc is { } entregue ? DevKitPageDb.Utc(entregue) : null,
            c.ConcluidoEmUtc is { } concluido ? DevKitPageDb.Utc(concluido) : null,
            c.Erro);

    internal static RespostaRemotaV1 Leitura(RespostaRemota r)
        => new(r.RespostaId, r.MaquinaId, r.ComandoId.Length == 0 ? null : r.ComandoId, r.Tipo, r.Texto, DevKitPageDb.Utc(r.GeradaEmUtc), DevKitPageDb.Utc(r.RecebidaEmUtc));

    internal static FalaRemotaV1 Leitura(FalaRemota f)
        => new(f.FalaId, f.MaquinaId, f.Autor, f.Origem, f.Texto, f.ComandoId.Length == 0 ? null : f.ComandoId, DevKitPageDb.Utc(f.DitaEmUtc),
            DevKitPageDb.Utc(f.RecebidaEmUtc));
}

/// <summary>A retenção da conversa (<see cref="OpcoesDoRemoto.RetencaoDaConversaDias"/>): o comando, a resposta, a fala e o evento, pela data deles.</summary>
public sealed class ExpurgoDaConversa(DevKitPageDb db, TimeProvider relogio, IOptions<OpcoesDoRemoto> opcoes) : IExpurgoDaConversa
{
    public async Task<int> ExpurgarAsync(CancellationToken ct)
    {
        var corte = relogio.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, opcoes.Value.RetencaoDaConversaDias));
        var eventos = await db.EventosDaConversa.Where(e => e.EmUtc < corte).ExecuteDeleteAsync(ct);
        var respostas = await db.RespostasRemotas.Where(r => r.RecebidaEmUtc < corte).ExecuteDeleteAsync(ct);
        var falas = await db.FalasRemotas.Where(f => f.RecebidaEmUtc < corte).ExecuteDeleteAsync(ct);
        var comandos = await db.ComandosRemotos.Where(c => c.CriadoEmUtc < corte).ExecuteDeleteAsync(ct);
        return eventos + respostas + falas + comandos;
    }
}

/// <summary>O aviso em memória desta instância da API: um <see cref="TaskCompletionSource"/> por usuário, trocado a cada aviso.</summary>
public sealed class AvisoDaConversa : IAvisoDaConversa
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _proximos = new();

    public void Avisar(int usuarioId)
    {
        if (_proximos.TryRemove(usuarioId, out var proximo))
            proximo.TrySetResult();
    }

    public Task ProximoAviso(int usuarioId)
        => _proximos.GetOrAdd(usuarioId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
}
