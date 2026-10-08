using System.Security.Claims;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

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
        // A máquina que JÁ existe só se registra de novo apresentando a chave atual (X-Machine-Key) ou pelo
        // admin; sem ela, 409 — o dev.kit que perdeu a chave se registra como máquina nova, com outro id.
        app.MapPost("/api/maquinas/registrar", async (
            MachineRegistrationV1 pedido, HttpContext http, ClaimsPrincipal quem, IOptions<OpcoesDeTelemetria> opcoes, IMaquinas maquinas, CancellationToken ct) =>
        {
            var maquinaId = (pedido.MaquinaId ?? string.Empty).Trim();
            if (maquinaId.Length is 0 or > 64)
                return Results.Problem("maquinaId é obrigatório (até 64 caracteres).", statusCode: StatusCodes.Status400BadRequest);

            var admin = quem.Identity?.IsAuthenticated == true && quem.IsInRole("admin") && !quem.HasClaim(Seguranca.ClaimTrocaPendente, "true");
            var codigo = opcoes.Value.CodigoDeRegistro ?? string.Empty;
            var porCodigo = codigo.Length > 0 && ChaveDeMaquina.Iguais(codigo, pedido.CodigoRegistro ?? string.Empty);
            if (!admin && !porCodigo)
                return Results.Problem("Registro recusado: informe o código de registro (ou use um token de admin).", statusCode: StatusCodes.Status401Unauthorized);

            var chaveAtual = http.Request.Headers[ContratoV1.CabecalhoDaChave].ToString();
            if (await maquinas.RegistrarAsync(maquinaId, pedido.VersaoDevKit, pedido.CodigoEmpresa, pedido.Colaborador, chaveAtual, admin, ct) is not { } registro)
                return Results.Problem(
                    "Esta máquina já está registrada: registre de novo com a chave atual (X-Machine-Key), ou registre-se com outro id.",
                    statusCode: StatusCodes.Status409Conflict);
            return Results.Ok(new MachineRegistrationResponseV1(registro.Chave, registro.Empresa, registro.Adesao));
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

    /// <summary>
    /// O painel. Toda rota lê o <see cref="EscopoDoPainel"/> do token (US #381) e o passa às consultas,
    /// que o aplicam num ponto só; o filtro de máquina fora do escopo é 403, e não uma lista vazia — o
    /// gestor não sonda outra empresa pelo id. O que é só do admin (a reação aos erros, os pedidos de
    /// demonstração) está sob a <see cref="Seguranca.PoliticaAdmin"/>.
    /// </summary>
    public static void MapearPainel(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/dashboard").WithTags("Dashboard");

        grupo.MapGet("/maquinas", (ClaimsPrincipal quem, IConsultasDoPainel consultas, CancellationToken ct)
            => ComEscopo(quem, consultas, null, async escopo => Results.Ok(await consultas.MaquinasAsync(escopo, ct)), ct));

        grupo.MapGet("/quantidade", (DateOnly? de, DateOnly? ate, int? maquina, ClaimsPrincipal quem, IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct)
            => ComEscopo(quem, consultas, maquina, async escopo => Results.Ok(await consultas.QuantidadeAsync(escopo, Periodo.Pedido(de, ate, Hoje(relogio)), maquina, ct)), ct));

        grupo.MapGet("/qualidade", (DateOnly? de, DateOnly? ate, int? maquina, ClaimsPrincipal quem, IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct)
            => ComEscopo(quem, consultas, maquina, async escopo => Results.Ok(await consultas.QualidadeAsync(escopo, Periodo.Pedido(de, ate, Hoje(relogio)), maquina, ct)), ct));

        grupo.MapGet("/eventos", (DateOnly? de, DateOnly? ate, int? maquina, int? pagina, int? tamanho, ClaimsPrincipal quem, IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct)
            => ComEscopo(quem, consultas, maquina, async escopo => Results.Ok(await consultas.EventosAsync(escopo, Periodo.Pedido(de, ate, Hoje(relogio)), maquina, pagina ?? 1, tamanho ?? 50, ct)), ct));

        // As exceções não classificadas (US #381): os grupos, o detalhe com o trace, a reação e a
        // exportação — o dado para abrir o Bug ou escrever o detector que tira o grupo daqui.
        grupo.MapGet("/erros", (DateOnly? de, DateOnly? ate, int? maquina, int? pagina, int? tamanho, ClaimsPrincipal quem, IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct)
            => ComEscopo(quem, consultas, maquina, async escopo => Results.Ok(await consultas.ErrosAsync(escopo, Periodo.Pedido(de, ate, Hoje(relogio)), maquina, pagina ?? 1, tamanho ?? 20, ct)), ct));

        grupo.MapGet("/erros/{id:long}", (long id, DateOnly? de, DateOnly? ate, ClaimsPrincipal quem, IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct)
            => ComErroVisivel(quem, consultas, id, async escopo =>
                await consultas.ErroAsync(escopo, id, Periodo.Pedido(de, ate, Hoje(relogio)), ct) is { } detalhe ? Results.Ok(detalhe) : Results.NotFound(), ct));

        grupo.MapPut("/erros/{id:long}/estado", async (long id, AlterarEstadoDoGrupo? pedido, IReacaoAErros reacao, CancellationToken ct) =>
        {
            var problema = RegrasDeErro.Validar(pedido);
            if (problema.Length > 0)
                return Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest);
            return await reacao.AlterarEstadoAsync(id, pedido!, ct) ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization(Seguranca.PoliticaAdmin);

        grupo.MapGet("/erros/{id:long}/exportar", (long id, DateOnly? de, DateOnly? ate, ClaimsPrincipal quem, IConsultasDoPainel consultas,
                IAuditoriaDeAcesso auditoria, TimeProvider relogio, CancellationToken ct)
            => ComErroVisivel(quem, consultas, id, async escopo =>
            {
                var periodo = Periodo.Pedido(de, ate, Hoje(relogio));
                if (await consultas.ErroAsync(escopo, id, periodo, ct) is not { } detalhe)
                    return Results.NotFound();

                // A exportação leva os nomes das máquinas e as ocorrências: é acesso a dado, e fica na trilha como a do uso.
                await auditoria.RegistrarAsync(Seguranca.UsuarioId(quem) ?? 0, quem.FindFirstValue(JwtRegisteredClaimNames.Name) ?? string.Empty, escopo,
                    $"exportar json do grupo de exceção {detalhe.Grupo.Assinatura} de {periodo.De:yyyy-MM-dd} a {periodo.Ate:yyyy-MM-dd}", detalhe.Ocorrencias.Count, ct);
                var json = JsonSerializer.SerializeToUtf8Bytes(detalhe, Exportacao);
                return Results.File(json, "application/json", $"excecao-{detalhe.Grupo.Assinatura}.json");
            }, ct)).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        // O plano empresarial (US #381): os colaboradores que consentiram e a COLETA — a exportação
        // por colaborador e dia, em CSV ou JSON, com cada acesso registrado na trilha de auditoria.
        grupo.MapGet("/colaboradores", (ClaimsPrincipal quem, IConsultasDoPainel consultas, CancellationToken ct)
            => ComEscopo(quem, consultas, null, async escopo => Results.Ok(await consultas.ColaboradoresAsync(escopo, ct)), ct));

        grupo.MapGet("/exportar", (string? formato, DateOnly? de, DateOnly? ate, int? maquina, ClaimsPrincipal quem,
                IConsultasDoPainel consultas, IAuditoriaDeAcesso auditoria, TimeProvider relogio, CancellationToken ct)
            => ComEscopo(quem, consultas, maquina, async escopo =>
            {
                var csv = !string.Equals(formato, "json", StringComparison.OrdinalIgnoreCase);
                if (formato is not null && csv && !string.Equals(formato, "csv", StringComparison.OrdinalIgnoreCase))
                    return Results.Problem("formato é csv ou json.", statusCode: StatusCodes.Status400BadRequest);

                var periodo = Periodo.Pedido(de, ate, Hoje(relogio));
                var linhas = await consultas.ExportarAsync(escopo, periodo, maquina, ct);
                var oQue = $"exportar {(csv ? "csv" : "json")} de {periodo.De:yyyy-MM-dd} a {periodo.Ate:yyyy-MM-dd}" + (maquina is null ? string.Empty : $" máquina {maquina}");
                await auditoria.RegistrarAsync(Seguranca.UsuarioId(quem) ?? 0, quem.FindFirstValue(JwtRegisteredClaimNames.Name) ?? string.Empty, escopo, oQue, linhas.Count, ct);

                var nome = $"uso-dos-colaboradores-{periodo.De:yyyyMMdd}-{periodo.Ate:yyyyMMdd}";
                return csv
                    ? Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(ExportacaoCsv.Gerar(linhas))).ToArray(), "text/csv", nome + ".csv")
                    : Results.File(JsonSerializer.SerializeToUtf8Bytes(linhas, Exportacao), "application/json", nome + ".json");
            }, ct)).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        // O ROI por work item (US #387): a foto mais recente de cada (máquina, item) que o dev.kit calculou.
        grupo.MapGet("/roi", (DateOnly? de, DateOnly? ate, int? maquina, ClaimsPrincipal quem, IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct)
            => ComEscopo(quem, consultas, maquina, async escopo => Results.Ok(await consultas.RoiAsync(escopo, Periodo.Pedido(de, ate, Hoje(relogio)), maquina, ct)), ct));

        // Os pedidos de demonstração: ler e excluir (eliminação a pedido do titular) — contatos de
        // venda, só do admin (o gestor de uma empresa cliente não os vê).
        grupo.MapGet("/demonstracoes", (int? pagina, int? tamanho, IPedidosDeDemonstracao pedidos, CancellationToken ct)
            => pedidos.ListarAsync(pagina ?? 1, tamanho ?? 20, ct)).RequireAuthorization(Seguranca.PoliticaAdmin);

        grupo.MapDelete("/demonstracoes/{id:long}", async (long id, IPedidosDeDemonstracao pedidos, CancellationToken ct)
            => await pedidos.ExcluirAsync(id, ct) ? Results.NoContent() : Results.NotFound()).RequireAuthorization(Seguranca.PoliticaAdmin);
    }

    /// <summary>As empresas e os gestores (US #381): só o admin cadastra.</summary>
    public static void MapearEmpresas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/empresas").WithTags("Empresas").RequireAuthorization(Seguranca.PoliticaAdmin);

        grupo.MapGet("/", (IEmpresas empresas, CancellationToken ct) => empresas.ListarAsync(ct));

        grupo.MapPost("/", async (EmpresaNova? empresa, IEmpresas empresas, CancellationToken ct) =>
        {
            var erros = ValidadorDeEmpresa.Validar(empresa);
            if (erros.Count > 0)
                return Results.ValidationProblem(erros, "Confira os dados da empresa.");
            var criada = await empresas.CriarAsync(empresa!, ct);
            return Results.Created($"/api/empresas/{criada.Id}", criada);
        }).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        grupo.MapPost("/{id:int}/gestores", async (int id, GestorNovo? gestor, IEmpresas empresas, CancellationToken ct) =>
        {
            var problema = ValidadorDeEmpresa.ValidarLogin(gestor?.Login);
            if (problema.Length > 0)
                return Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest);

            var (criado, erro) = await empresas.CriarGestorAsync(id, gestor!.Login, ct);
            if (erro.Length > 0)
                return Results.Problem(erro, statusCode: StatusCodes.Status409Conflict);
            return criado is null ? Results.NotFound() : Results.Created($"/api/empresas/{id}/gestores/{criado.Id}", criado);
        }).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);
    }

    /// <summary>
    /// Roda a consulta com o escopo do token: sem escopo válido, 403; com filtro de máquina fora do
    /// escopo (outra empresa, ou sem consentimento), 403.
    /// </summary>
    private static async Task<IResult> ComEscopo(
        ClaimsPrincipal quem, IConsultasDoPainel consultas, int? maquina, Func<EscopoDoPainel, Task<IResult>> consulta, CancellationToken ct)
    {
        if (Seguranca.Escopo(quem) is not { } escopo)
            return Results.Forbid();
        if (maquina is { } id && !await consultas.MaquinaVisivelAsync(escopo, id, ct))
            return Results.Forbid();
        return await consulta(escopo);
    }

    /// <summary>Roda a consulta de um grupo de erro só se ele tiver ocorrência no escopo — senão 403 para o gestor, 404 para o admin.</summary>
    private static async Task<IResult> ComErroVisivel(
        ClaimsPrincipal quem, IConsultasDoPainel consultas, long id, Func<EscopoDoPainel, Task<IResult>> consulta, CancellationToken ct)
    {
        if (Seguranca.Escopo(quem) is not { } escopo)
            return Results.Forbid();
        if (!await consultas.ErroVisivelAsync(escopo, id, ct))
            return escopo.EhAdmin ? Results.NotFound() : Results.Forbid();
        return await consulta(escopo);
    }

    public static void MapearDemonstracoes(this IEndpointRouteBuilder app)
    {
        // A única escrita anônima da API: validada antes do banco e limitada por IP do cliente.
        app.MapPost("/api/demonstracoes", async (PedidoDeDemonstracaoV1? pedido, IPedidosDeDemonstracao pedidos, CancellationToken ct) =>
        {
            var erros = ValidadorDeDemonstracao.Validar(pedido);
            if (erros.Count > 0)
                return Results.ValidationProblem(erros, "Confira os campos do pedido.");

            var criado = await pedidos.RegistrarAsync(pedido!, ct);
            return Results.Created($"/api/dashboard/demonstracoes/{criado.Id}", criado);
        }).AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDoFormulario).WithTags("Demonstrações");
    }

    private static DateOnly Hoje(TimeProvider relogio) => DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime);

    /// <summary>O JSON dos arquivos exportados: o mesmo camelCase da API, indentado para quem abre o arquivo.</summary>
    private static readonly JsonSerializerOptions Exportacao = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
