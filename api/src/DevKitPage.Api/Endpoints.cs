using System.Security.Claims;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api;

/// <summary>As rotas da API. Cada grupo diz a sua autorização; o resto cai na política padrão (usuário sem troca pendente).</summary>
public static class Endpoints
{
    public static void MapearAutenticacao(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth").WithTags("Autenticação");

        grupo.MapPost("/login", async (LoginRequest pedido, IUsuarios usuarios, EmissorDeToken emissor, CancellationToken ct) =>
        {
            var (resultado, usuario) = await usuarios.AutenticarAsync(pedido.Login, pedido.Senha, ct);
            return resultado switch
            {
                ResultadoDoLogin.Ok => Results.Ok(emissor.Emitir(usuario!)),
                ResultadoDoLogin.Bloqueado => Results.Problem("Usuário bloqueado por excesso de tentativas. Tente de novo mais tarde.", statusCode: StatusCodes.Status423Locked),
                _ => Results.Problem("Login ou senha inválidos.", statusCode: StatusCodes.Status401Unauthorized),
            };
        }).AllowAnonymous();

        grupo.MapPost("/trocar-senha", async (TrocarSenhaRequest pedido, ClaimsPrincipal quem, IUsuarios usuarios, EmissorDeToken emissor, CancellationToken ct) =>
        {
            if (Seguranca.UsuarioId(quem) is not { } id)
                return Results.Unauthorized();

            var erro = await usuarios.TrocarSenhaAsync(id, pedido.SenhaAtual, pedido.NovaSenha, ct);
            if (erro.Length > 0)
                return Results.Problem(erro, statusCode: StatusCodes.Status400BadRequest);

            // Um token NOVO, sem a troca pendente: é ele que abre o dashboard.
            return Results.Ok(emissor.Emitir((await usuarios.ObterAsync(id, ct))!));
        }).RequireAuthorization(Seguranca.PoliticaAutenticado);
    }

    public static void MapearMaquinas(this IEndpointRouteBuilder app)
    {
        // Anônimo NA ROTA, e conferido no corpo: registra quem tem o código de registro configurado,
        // ou o admin autenticado (sem troca pendente).
        app.MapPost("/api/maquinas/registrar", async (
            MachineRegistrationV1 pedido, ClaimsPrincipal quem, IOptions<OpcoesDeTelemetria> opcoes, IMaquinas maquinas, CancellationToken ct) =>
        {
            var maquinaId = (pedido.MaquinaId ?? string.Empty).Trim();
            if (maquinaId.Length is 0 or > 64)
                return Results.Problem("maquinaId é obrigatório (até 64 caracteres).", statusCode: StatusCodes.Status400BadRequest);

            var admin = quem.Identity?.IsAuthenticated == true && quem.IsInRole("admin") && !quem.HasClaim(Seguranca.ClaimTrocaPendente, "true");
            var codigo = opcoes.Value.CodigoDeRegistro ?? string.Empty;
            var porCodigo = codigo.Length > 0 && ChaveDeMaquina.Iguais(codigo, pedido.CodigoRegistro ?? string.Empty);
            if (!admin && !porCodigo)
                return Results.Problem("Registro recusado: informe o código de registro (ou use um token de admin).", statusCode: StatusCodes.Status401Unauthorized);

            return Results.Ok(new MachineRegistrationResponseV1(await maquinas.RegistrarAsync(maquinaId, pedido.VersaoDevKit, ct)));
        }).AllowAnonymous().WithTags("Máquinas");
    }

    public static void MapearTelemetria(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/telemetria/lote", async (HttpContext http, IOptions<OpcoesDeTelemetria> opcoes, IIngestaoDeTelemetria ingestao, CancellationToken ct) =>
        {
            var limite = opcoes.Value.MaxBytesPorLote;
            if (http.Request.ContentLength > limite)
                return Results.Problem($"O lote passa de {limite} bytes.", statusCode: StatusCodes.Status413PayloadTooLarge);
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } corpo)
                corpo.MaxRequestBodySize = limite; // o corpo sem Content-Length (chunked) também para no limite

            TelemetryBatchV1? lote;
            try
            {
                lote = await http.Request.ReadFromJsonAsync<TelemetryBatchV1>(ct);
            }
            catch (JsonException ex)
            {
                return Results.Problem($"O corpo não é um lote v1 válido: {ex.Message}", statusCode: StatusCodes.Status400BadRequest);
            }

            var maquina = int.Parse(http.User.FindFirstValue(AutenticacaoDeMaquina.ClaimDaMaquina)!, System.Globalization.CultureInfo.InvariantCulture);
            var publica = http.User.FindFirstValue(AutenticacaoDeMaquina.ClaimDoIdPublico) ?? string.Empty;
            if (ValidadorDeLote.Validar(lote, publica, opcoes.Value.MaxEventosPorLote) is { } recusa)
                return Results.Problem(recusa.Mensagem, statusCode: recusa.Status);

            return Results.Accepted(value: await ingestao.RegistrarAsync(maquina, lote!, ct));
        }).RequireAuthorization(Seguranca.PoliticaMaquina).WithTags("Telemetria");
    }

    public static void MapearPainel(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/dashboard").WithTags("Dashboard");

        grupo.MapGet("/maquinas", (IConsultasDoPainel consultas, CancellationToken ct) => consultas.MaquinasAsync(ct));

        grupo.MapGet("/quantidade", (DateOnly? de, DateOnly? ate, int? maquina, IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct)
            => consultas.QuantidadeAsync(Periodo.Pedido(de, ate, Hoje(relogio)), maquina, ct));

        grupo.MapGet("/qualidade", (DateOnly? de, DateOnly? ate, int? maquina, IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct)
            => consultas.QualidadeAsync(Periodo.Pedido(de, ate, Hoje(relogio)), maquina, ct));

        grupo.MapGet("/eventos", (DateOnly? de, DateOnly? ate, int? maquina, int? pagina, int? tamanho, IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct)
            => consultas.EventosAsync(Periodo.Pedido(de, ate, Hoje(relogio)), maquina, pagina ?? 1, tamanho ?? 50, ct));
    }

    private static DateOnly Hoje(TimeProvider relogio) => DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime);
}
