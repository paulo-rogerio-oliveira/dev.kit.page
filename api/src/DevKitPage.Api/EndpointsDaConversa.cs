using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da conversa e dos comandos remotos (fase 2 do controle remoto, <c>docs/contrato-remoto-v1.md</c> seção 4). Todas
/// com o JWT do usuário, sem troca pendente; o dono é o usuário do token, e o comando, a máquina e a fila de outro são 404.
/// <list type="bullet">
/// <item>Do SITE: <c>POST /comandos</c> (202 o novo, 200 o reenvio com o mesmo id), <c>GET /comandos/{id}</c>,
/// <c>GET /maquinas/{maquinaId}/conversa</c> (o histórico e o último evento) e <c>GET /conversa/eventos</c> (o SSE, retomado
/// pelo <c>Last-Event-ID</c>);</item>
/// <item>Do DEV.KIT: <c>POST /fila/sas</c> (a SAS curta, só da fila do usuário e só de consumo), <c>POST /comandos/{id}/recebido</c>
/// (o primeiro recebimento dentro do TTL manda executar; o repetido e o vencido, não), <c>POST /respostas</c> (201 a nova, 200
/// a reenviada) e <c>POST /conversa/falas</c> (a fala do chat do dev.kit com o autor e a origem: 201 a nova, 200 a reenviada,
/// 409 <c>id-em-uso</c> o mesmo id com outro conteúdo; limite próprio por minuto). Estas quatro aceitam também o token da
/// máquina (<see cref="Seguranca.PoliticaRemotoDaMaquina"/>), e só para o <c>maquinaId</c> dela (403 para outra).</item>
/// </list>
/// As recusas: 400 (forma, contrato), 404, 409 com <c>codigo</c> (sem fila, turno em andamento, outra máquina, id em uso), 413,
/// 415, 422 (dado sensível), 429 (o limite de comandos por usuário, com <c>Retry-After</c>; e o de envios por minuto) e 503 (a
/// fila não aceitou: o comando volta gravado como <c>falhou</c>).
/// </summary>
public static class EndpointsDaConversa
{
    public static void MapearConversa(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/remoto").WithTags("Controle remoto");

        grupo.MapPost("/comandos", (HttpContext http, ClaimsPrincipal quem, IConversaRemota conversa, IOptions<OpcoesDoRemoto> opcoes, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                var (comando, recusa) = await CorpoJson.LerAsync<ComandoNovoV1>(http.Request, ConversaV1.TamanhoMaximoDoEnvio, ct);
                if (recusa is not null)
                    return recusa;
                if (EndpointsDoRemoto.Recusar(RegrasDaConversa.Validar(comando, opcoes.Value), () => RegrasDaConversa.CamposSensiveis(comando!)) is { } problema)
                    return problema;
                var resultado = await conversa.EnviarAsync(usuario, comando!, ct);
                if (resultado.Falha != FalhaNaConversa.Nenhuma)
                    return Falha(http, resultado);
                var aceito = resultado.Valor!;
                return aceito.Novo
                    ? Results.Accepted($"/api/remoto/comandos/{aceito.Comando.Id}", aceito.Comando)
                    : Results.Ok(aceito.Comando);
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDaConversa);

        grupo.MapGet("/comandos/{comandoId}", (string comandoId, ClaimsPrincipal quem, IConversaRemota conversa, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
                RegrasDaConversa.IdValido(comandoId) && await conversa.ComandoAsync(usuario, comandoId, ct) is { } comando
                    ? Results.Ok(comando)
                    : Results.Problem("Comando não encontrado.", statusCode: StatusCodes.Status404NotFound)));

        grupo.MapGet("/maquinas/{maquinaId}/conversa", (string maquinaId, ClaimsPrincipal quem, IConversaRemota conversa, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
                RegrasDoRemoto.MaquinaIdValido(maquinaId) && await conversa.ConversaAsync(usuario, maquinaId, ct) is { } historico
                    ? Results.Ok(historico)
                    : Results.Problem("Máquina não encontrada, ou não é sua.", statusCode: StatusCodes.Status404NotFound)));

        // (semana 7) A conversa com o agente de UMA task: a sessão é o id da execução no retrato. Os eventos novos dela vêm no
        // mesmo SSE da máquina, com a sessão na fala e no comando (o site separa).
        grupo.MapGet("/maquinas/{maquinaId}/sessoes/{sessao}/conversa", (string maquinaId, string sessao, ClaimsPrincipal quem,
                IConversaRemota conversa, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
                RegrasDoRemoto.MaquinaIdValido(maquinaId) && RegrasDaConversa.SessaoValida(sessao)
                    && await conversa.ConversaDaTaskAsync(usuario, maquinaId, sessao, ct) is { } historico
                    ? Results.Ok(historico)
                    : Results.Problem("Máquina não encontrada, ou não é sua.", statusCode: StatusCodes.Status404NotFound)));

        grupo.MapGet("/conversa/eventos", (HttpContext http, string? maquinaId, long? desde, ClaimsPrincipal quem, IConversaRemota conversa,
                IAvisoDaConversa aviso, IOptions<OpcoesDoRemoto> opcoes, CancellationToken ct) =>
            {
                if (Seguranca.UsuarioId(quem) is not { } usuario)
                    return Results.Forbid();
                if (maquinaId is not null && !RegrasDoRemoto.MaquinaIdValido(maquinaId))
                    return Results.Problem($"O maquinaId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).", statusCode: StatusCodes.Status400BadRequest);
                // O proxy não segura o fluxo (o nginx e afins respeitam este cabeçalho).
                http.Response.Headers["X-Accel-Buffering"] = "no";
                return TypedResults.ServerSentEvents(Eventos(usuario, maquinaId, UltimoEvento(http.Request) ?? desde ?? 0, conversa, aviso, opcoes.Value, ct));
            });

        grupo.MapPost("/fila/sas", (HttpContext http, ClaimsPrincipal quem, IConversaRemota conversa, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                var (pedido, recusa) = await CorpoJson.LerAsync<PedidoDeSasV1>(http.Request, ConversaV1.TamanhoMaximoDoEnvio, ct);
                if (recusa is not null)
                    return recusa;
                var forma = RegrasDoRemoto.MaquinaIdValido(pedido!.MaquinaId) ? string.Empty : $"O maquinaId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
                if (EndpointsDoRemoto.Recusar(pedido.Contrato, forma, () => []) is { } problema)
                    return problema;
                if (TokenDaMaquina.OutraMaquina(quem, pedido.MaquinaId) is { } outra)
                    return outra;
                var resultado = await conversa.SasAsync(usuario, pedido.MaquinaId, ct);
                return resultado.Falha == FalhaNaConversa.Nenhuma ? Results.Ok(resultado.Valor) : Falha(http, resultado);
            })).RequireAuthorization(Seguranca.PoliticaRemotoDaMaquina).RequireRateLimiting(LimiteDeTaxa.PoliticaDaConversa);

        grupo.MapPost("/comandos/{comandoId}/recebido", (string comandoId, HttpContext http, ClaimsPrincipal quem, IConversaRemota conversa, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                var (recebimento, recusa) = await CorpoJson.LerAsync<RecebimentoV1>(http.Request, ConversaV1.TamanhoMaximoDoEnvio, ct);
                if (recusa is not null)
                    return recusa;
                var forma = RegrasDoRemoto.MaquinaIdValido(recebimento!.MaquinaId) ? string.Empty : $"O maquinaId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
                if (EndpointsDoRemoto.Recusar(recebimento.Contrato, forma, () => []) is { } problema)
                    return problema;
                if (!RegrasDaConversa.IdValido(comandoId))
                    return Results.Problem("Comando não encontrado.", statusCode: StatusCodes.Status404NotFound);
                if (TokenDaMaquina.OutraMaquina(quem, recebimento.MaquinaId) is { } outra)
                    return outra;
                var resultado = await conversa.ReceberAsync(usuario, comandoId, recebimento.MaquinaId, ct);
                return resultado.Falha == FalhaNaConversa.Nenhuma ? Results.Ok(resultado.Valor) : Falha(http, resultado);
            })).RequireAuthorization(Seguranca.PoliticaRemotoDaMaquina).RequireRateLimiting(LimiteDeTaxa.PoliticaDaConversa);

        grupo.MapPost("/respostas", (HttpContext http, ClaimsPrincipal quem, IConversaRemota conversa, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                var (resposta, recusa) = await CorpoJson.LerAsync<RespostaDoDevKitV1>(http.Request, ConversaV1.TamanhoMaximoDoEnvio, ct);
                if (recusa is not null)
                    return recusa;
                if (EndpointsDoRemoto.Recusar(resposta!.Contrato, RegrasDaConversa.Validar(resposta), () => RegrasDaConversa.CamposSensiveis(resposta)) is { } problema)
                    return problema;
                if (TokenDaMaquina.OutraMaquina(quem, resposta.MaquinaId) is { } outra)
                    return outra;
                var resultado = await conversa.ResponderAsync(usuario, resposta, ct);
                if (resultado.Falha != FalhaNaConversa.Nenhuma)
                    return Falha(http, resultado);
                return resultado.Valor!.Repetida ? Results.Ok(resultado.Valor) : Results.Created((string?)null, resultado.Valor);
            })).RequireAuthorization(Seguranca.PoliticaRemotoDaMaquina).RequireRateLimiting(LimiteDeTaxa.PoliticaDaConversa);

        grupo.MapPost("/conversa/falas", (HttpContext http, ClaimsPrincipal quem, IConversaRemota conversa, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                var (fala, recusa) = await CorpoJson.LerAsync<FalaDoDevKitV1>(http.Request, ConversaV1.TamanhoMaximoDoEnvio, ct);
                if (recusa is not null)
                    return recusa;
                if (EndpointsDoRemoto.Recusar(fala!.Contrato, RegrasDaConversa.Validar(fala), () => RegrasDaConversa.CamposSensiveis(fala)) is { } problema)
                    return problema;
                if (TokenDaMaquina.OutraMaquina(quem, fala.MaquinaId) is { } outra)
                    return outra;
                var resultado = await conversa.FalarAsync(usuario, fala, ct);
                if (resultado.Falha != FalhaNaConversa.Nenhuma)
                    return Falha(http, resultado);
                return resultado.Valor!.Repetida ? Results.Ok(resultado.Valor) : Results.Created((string?)null, resultado.Valor);
            })).RequireAuthorization(Seguranca.PoliticaRemotoDaMaquina).RequireRateLimiting(LimiteDeTaxa.PoliticaDasFalas);
    }

    /// <summary>
    /// O fluxo do SSE: os eventos do usuário depois do cursor, um a um com o <c>id:</c>, e a espera pelo aviso desta instância
    /// (ou pelo <see cref="OpcoesDoRemoto.IntervaloDoSseSegundos"/>, o que vier antes). O TTL vencido vira evento aqui também. Na
    /// espera sem evento vai um <c>ping</c> sem id; depois de <see cref="OpcoesDoRemoto.DuracaoDoSseSegundos"/> o fluxo fecha, e o
    /// site reconecta com o <c>Last-Event-ID</c>.
    /// </summary>
    private static async IAsyncEnumerable<SseItem<EventoDaConversaV1?>> Eventos(
        int usuario, string? maquinaId, long cursor, IConversaRemota conversa, IAvisoDaConversa aviso, OpcoesDoRemoto opcoes,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const int Lote = 100;
        var intervalo = TimeSpan.FromSeconds(Math.Clamp(opcoes.IntervaloDoSseSegundos, 1, 60));
        var fim = DateTime.UtcNow.AddSeconds(Math.Clamp(opcoes.DuracaoDoSseSegundos, 1, 3600));
        var silencio = TimeSpan.Zero;
        while (!ct.IsCancellationRequested && DateTime.UtcNow < fim)
        {
            var proximo = aviso.ProximoAviso(usuario);
            await conversa.ExpirarVencidosAsync(usuario, ct);
            var eventos = await conversa.EventosAsync(usuario, maquinaId, cursor, Lote, ct);
            foreach (var evento in eventos)
            {
                cursor = evento.Id;
                yield return new SseItem<EventoDaConversaV1?>(evento, evento.Tipo) { EventId = evento.Id.ToString(CultureInfo.InvariantCulture) };
            }
            if (eventos.Count == Lote)
                continue;

            if (eventos.Count > 0)
                silencio = TimeSpan.Zero;
            else if ((silencio += intervalo) >= TimeSpan.FromSeconds(15))
            {
                silencio = TimeSpan.Zero;
                yield return new SseItem<EventoDaConversaV1?>(null, "ping");
            }

            if (!await EsperarAsync(proximo, intervalo, ct))
                yield break;
        }
    }

    /// <summary>Espera o aviso ou o intervalo; falso quando o cliente fechou.</summary>
    private static async Task<bool> EsperarAsync(Task aviso, TimeSpan intervalo, CancellationToken ct)
    {
        try
        {
            await aviso.WaitAsync(intervalo, ct);
        }
        catch (TimeoutException)
        {
            // Sem aviso nesta instância: relê o SQL (o evento gravado por outra instância chega assim).
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        return true;
    }

    /// <summary>O <c>Last-Event-ID</c> da reconexão do SSE; nulo sem ele (ou ilegível).</summary>
    private static long? UltimoEvento(HttpRequest pedido)
        => long.TryParse(pedido.Headers["Last-Event-ID"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>A recusa da conversa no status certo, com o <c>codigo</c>; o 503 leva o comando gravado, e o 429 o <c>Retry-After</c>.</summary>
    private static IResult Falha<T>(HttpContext http, ResultadoDaConversa<T> resultado) where T : class
    {
        var (status, codigo) = resultado.Falha switch
        {
            FalhaNaConversa.NaoEncontrado => (StatusCodes.Status404NotFound, (string?)null),
            FalhaNaConversa.SemFila => (StatusCodes.Status409Conflict, ConversaV1.CodigoSemFila),
            FalhaNaConversa.TurnoEmAndamento => (StatusCodes.Status409Conflict, ConversaV1.CodigoTurnoEmAndamento),
            FalhaNaConversa.IdEmUso => (StatusCodes.Status409Conflict, ConversaV1.CodigoIdEmUso),
            FalhaNaConversa.OutraMaquina => (StatusCodes.Status409Conflict, ConversaV1.CodigoOutraMaquina),
            FalhaNaConversa.Limite => (StatusCodes.Status429TooManyRequests, ConversaV1.CodigoLimiteDeEnvio),
            FalhaNaConversa.FilaIndisponivel => (StatusCodes.Status503ServiceUnavailable, ConversaV1.CodigoFilaIndisponivel),
            _ => (StatusCodes.Status400BadRequest, null),
        };
        var extensoes = new Dictionary<string, object?>();
        if (codigo is not null)
            extensoes["codigo"] = codigo;
        if (resultado.Valor is ComandoAceito aceito)
            extensoes["comando"] = aceito.Comando;
        if (resultado.TenteEm is { } espera)
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return Results.Problem(resultado.Mensagem, statusCode: status, extensions: extensoes);
    }
}
