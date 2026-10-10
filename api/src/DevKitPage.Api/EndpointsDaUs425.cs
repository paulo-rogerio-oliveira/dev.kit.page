using System.Security.Claims;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da US #425: o catálogo básico (o proxy das releases <c>catalogo-vN</c>) e o da empresa, a conta do dev
/// (adesão, link de senha, <c>/conta</c>, a empresa do gestor), a licença assinada e o gateway de pagamento.
/// </summary>
public static class EndpointsDaUs425
{
    /// <summary>
    /// O catálogo BÁSICO (fase 1): anônimo como a última versão do app, pelo MESMO caminho das releases — o token só
    /// no servidor, a mesma lista em cache e o download em streaming. A delta (<c>?desde=N</c>) traz só o que mudou,
    /// e 304 sem mudança. As releases do catálogo para o admin acompanhar ficam sob a <see cref="Seguranca.PoliticaAdmin"/>.
    /// </summary>
    public static void MapearCatalogoBase(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/catalogo/base").WithTags("Catálogo básico");

        grupo.MapGet("/", async (int? desde, HttpContext http, IReleasesDoDevKit releases, CancellationToken ct) =>
        {
            if (await UltimoAsync(releases, ct) is not { } ultimo)
                return CatalogoIndisponivel();
            http.Response.Headers.ETag = $"\"{ultimo.Release.Tag}\"";
            var inicio = Math.Max(0, desde ?? 0);
            if (RegrasDoCatalogoBase.Delta(ultimo.Manifesto, inicio) is not { } delta)
                return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Ok(new CatalogoBaseV1(inicio, delta, "/api/catalogo/base/download", ultimo.Release.Sha256));
        }).AllowAnonymous();

        grupo.MapGet("/download", async (HttpContext http, IReleasesDoDevKit releases, CancellationToken ct) =>
        {
            if (await releases.CatalogosAsync(ct) is not [var ultima, ..])
                return CatalogoIndisponivel();
            await using var pacote = await releases.AbrirPacoteAsync(ultima, ct);
            if (pacote is null)
                return CatalogoIndisponivel();
            await Transmitir(http, pacote, $"catalogo-{ultima.Versao}.zip", ct);
            return Results.Empty;
        }).AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDoDownload);

        grupo.MapGet("/vitrine", async (IReleasesDoDevKit releases, CancellationToken ct) =>
            await UltimoAsync(releases, ct) is { } ultimo
                ? Results.Ok(RegrasDoCatalogoBase.Vitrine(ultimo.Manifesto, ultimo.Release.PublicadaEm))
                : CatalogoIndisponivel()).AllowAnonymous();

        grupo.MapGet("/releases", async (IReleasesDoDevKit releases, CancellationToken ct) =>
        {
            if (await releases.CatalogosAsync(ct) is not { } lista)
                return CatalogoIndisponivel();
            var linhas = new List<ReleaseDoCatalogoV1>(lista.Count);
            foreach (var r in lista)
            {
                var manifesto = await releases.ManifestoAsync(r, ct);
                linhas.Add(new ReleaseDoCatalogoV1(r.Versao, r.Tag, r.Nome, r.PublicadaEm, manifesto?.Itens.Count ?? 0, manifesto?.Retirados.Count ?? 0, r.TamanhoBytes, r.Sha256));
            }

            return Results.Ok(linhas);
        }).RequireAuthorization(Seguranca.PoliticaAdmin);
    }

    /// <summary>
    /// O catálogo da EMPRESA (fase 2): o dev e o gestor de uma empresa publicam, listam, retiram, assinam e desistem —
    /// e a sincronia do dev.kit lê os assinados. O isolamento é o <see cref="EscopoDaEmpresa"/> do token, aplicado num
    /// ponto só no <c>CatalogoDaEmpresa</c>: o item de outra empresa é 403.
    /// </summary>
    public static void MapearCatalogoDaEmpresa(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/catalogo/empresa").WithTags("Catálogo da empresa");

        grupo.MapGet("/", (ClaimsPrincipal quem, ICatalogoDaEmpresa catalogo, CancellationToken ct)
            => ComEmpresa(quem, async escopo => Results.Ok(await catalogo.ListarAsync(escopo, ct))));

        grupo.MapPost("/", (PublicacaoV1? publicacao, ClaimsPrincipal quem, ICatalogoDaEmpresa catalogo, IReleasesDoDevKit releases, CancellationToken ct)
            => ComEmpresa(quem, async escopo =>
            {
                var problema = RegrasDoCatalogoDaEmpresa.Validar(publicacao, await NomesDoBasicoAsync(releases, ct));
                if (problema.Length > 0)
                    return Results.Problem(problema, statusCode: problema.StartsWith("Já existe", StringComparison.Ordinal) ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
                var feita = await catalogo.PublicarAsync(escopo, publicacao!, ct);
                return feita.Valor is { } valor
                    ? valor.Nova ? Results.Created($"/api/catalogo/empresa/{valor.Id}", valor) : Results.Ok(valor)
                    : Falha(feita);
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        grupo.MapDelete("/{id:long}", (long id, ClaimsPrincipal quem, ICatalogoDaEmpresa catalogo, CancellationToken ct)
            => ComEmpresa(quem, async escopo => Resultado(await catalogo.RetirarAsync(escopo, id, ct))));

        grupo.MapPost("/{id:long}/assinatura", (long id, ClaimsPrincipal quem, ICatalogoDaEmpresa catalogo, CancellationToken ct)
            => ComEmpresa(quem, async escopo => Resultado(await catalogo.AssinarAsync(escopo, id, ct))));

        grupo.MapDelete("/{id:long}/assinatura", (long id, ClaimsPrincipal quem, ICatalogoDaEmpresa catalogo, CancellationToken ct)
            => ComEmpresa(quem, async escopo => Resultado(await catalogo.DesistirAsync(escopo, id, ct))));

        grupo.MapGet("/assinados", (ClaimsPrincipal quem, ICatalogoDaEmpresa catalogo, CancellationToken ct)
            => ComEmpresa(quem, async escopo => Results.Ok(await catalogo.AssinadosAsync(escopo, ct))));
    }

    /// <summary>
    /// A conta do dev (fase 3). A adesão, o pedido de link e a definição da senha são ANÔNIMOS (o dev ainda não tem
    /// senha) e limitados por IP; <c>/api/conta</c> é de quem está logado; <c>/api/empresa</c> é do gestor, no escopo
    /// da empresa dele.
    /// </summary>
    public static void MapearConta(this IEndpointRouteBuilder app)
    {
        var conta = app.MapGroup("/api/conta").WithTags("Conta");

        conta.MapPost("/adesao", async (AdesaoV1? adesao, IContas contas, CancellationToken ct) =>
        {
            var erros = RegrasDaConta.Validar(adesao);
            if (erros.Count > 0)
                return Results.ValidationProblem(erros, "Confira os dados da adesão.");
            var feita = await contas.AderirAsync(adesao!, ct);
            return feita.Valor is { } valor ? Results.Accepted(value: valor) : Falha(feita);
        }).AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDaConta);

        // A mesma resposta exista ou não o e-mail: a rota não serve para descobrir quem tem conta.
        conta.MapPost("/link", async (PedidoDeLinkV1? pedido, IContas contas, CancellationToken ct) =>
        {
            await contas.PedirLinkAsync(pedido?.Email ?? string.Empty, ct);
            return Results.Accepted(value: new AdesaoFeitaV1("Se o e-mail tiver uma conta, o link para definir a senha chega em instantes."));
        }).AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDaConta);

        conta.MapPost("/definir-senha", async (DefinirSenhaV1? pedido, IContas contas, CancellationToken ct) =>
        {
            var erro = await contas.DefinirSenhaAsync(pedido?.Token ?? string.Empty, pedido?.NovaSenha ?? string.Empty, ct);
            return erro.Length > 0 ? Results.Problem(erro, statusCode: StatusCodes.Status400BadRequest) : Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDaConta);

        conta.MapGet("/", async (ClaimsPrincipal quem, IContas contas, CancellationToken ct)
            => Seguranca.UsuarioId(quem) is { } id && await contas.ContaAsync(id, ct) is { } dados ? Results.Ok(dados) : Results.Unauthorized());

        var empresa = app.MapGroup("/api/empresa").WithTags("Empresa do gestor");

        empresa.MapGet("/", (ClaimsPrincipal quem, IGestaoDaEmpresa gestao, CancellationToken ct)
            => ComGestor(quem, async escopo => await gestao.EmpresaAsync(escopo, ct) is { } dados ? Results.Ok(dados) : Results.NotFound()));

        empresa.MapGet("/usuarios", (ClaimsPrincipal quem, IGestaoDaEmpresa gestao, CancellationToken ct)
            => ComGestor(quem, async escopo => Results.Ok(await gestao.UsuariosAsync(escopo, ct))));

        empresa.MapPut("/usuarios/{id:int}/bloqueio", (int id, BloqueioDoUsuario? pedido, ClaimsPrincipal quem, IGestaoDaEmpresa gestao, CancellationToken ct)
            => ComGestor(quem, async escopo => pedido is null
                ? Results.Problem("Informe se o usuário fica bloqueado.", statusCode: StatusCodes.Status400BadRequest)
                : Resultado(await gestao.BloquearAsync(escopo, id, pedido.Bloqueado, ct))));

        empresa.MapPost("/codigo", (ClaimsPrincipal quem, IGestaoDaEmpresa gestao, CancellationToken ct)
            => ComGestor(quem, async escopo => Results.Ok(await gestao.NovoCodigoAsync(escopo, ct)))).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        empresa.MapPut("/politica", (PoliticaDaEmpresaV1? pedido, ClaimsPrincipal quem, IGestaoDaEmpresa gestao, CancellationToken ct)
            => ComGestor(quem, async escopo => Results.Ok(await gestao.DefinirPoliticaAsync(escopo, pedido?.LoginObrigatorio, ct))));
    }

    /// <summary>
    /// A renovação da licença (fase 4): exige o login (o JWT do dev) E a chave da máquina (<c>X-Machine-Key</c>) — a
    /// licença é de um usuário NUMA máquina. Cada recusa tem o seu status e a sua mensagem.
    /// </summary>
    public static void MapearLicencas(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/licencas/renovar", async (RenovacaoV1? pedido, HttpContext http, ClaimsPrincipal quem, IMaquinas maquinas, ILicencas licencas, CancellationToken ct) =>
        {
            if (!licencas.Disponivel)
                return Results.Problem("A renovação da licença está indisponível no momento. Tente de novo em alguns minutos.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (Seguranca.UsuarioId(quem) is not { } usuario)
                return Results.Unauthorized();
            var chave = http.Request.Headers[ContratoV1.CabecalhoDaChave].ToString();
            if (await maquinas.AutenticarAsync(chave, ct) is not { } maquina)
                return Results.Problem("Informe a chave da máquina (X-Machine-Key): a licença é de um usuário numa máquina.", statusCode: StatusCodes.Status401Unauthorized);

            var (licenca, recusa) = await licencas.RenovarAsync(usuario, maquina.Id, pedido?.LicencaAtual, ct);
            return recusa switch
            {
                RecusaDaRenovacao.Nenhuma => Results.Ok(licenca),
                RecusaDaRenovacao.UsuarioBloqueado => Results.Problem("Usuário bloqueado: a licença não renova. Fale com o gestor.", statusCode: StatusCodes.Status403Forbidden),
                RecusaDaRenovacao.SemAssento => Results.Problem("A sua empresa não tem assento para você: a licença não renova. Fale com o gestor.", statusCode: StatusCodes.Status403Forbidden),
                RecusaDaRenovacao.SemEmpresa => Results.Problem("O seu usuário não é de uma empresa com assinatura: a licença não renova.", statusCode: StatusCodes.Status403Forbidden),
                RecusaDaRenovacao.AssinaturaCancelada => Results.Problem("A assinatura foi cancelada: a licença não renova.", statusCode: StatusCodes.Status402PaymentRequired),
                _ => Results.Problem("Ainda não é hora de renovar: a licença atual renova a partir do 7º dia.", statusCode: StatusCodes.Status409Conflict),
            };
        }).WithTags("Licenças");
    }

    /// <summary>
    /// A compra individual (fase 5): o checkout (anônimo, limitado por IP) e o aviso do gateway — anônimo NA ROTA e
    /// conferido no corpo pela assinatura HMAC; o id do evento torna o reenvio inofensivo.
    /// </summary>
    public static void MapearPagamentos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/pagamentos").WithTags("Pagamentos");

        grupo.MapPost("/checkout", (CheckoutV1? pedido, IPagamentos pagamentos) =>
        {
            var email = RegrasDaConta.Email(pedido?.Email);
            if (pedido is null || !ValidadorDeDemonstracao.EmailValido(email) || string.IsNullOrWhiteSpace(pedido.Plano) || pedido.Plano.Length > ValidadorDeEmpresa.TamanhoMaximoDoPlano)
                return Results.Problem("Informe um e-mail válido e o plano.", statusCode: StatusCodes.Status400BadRequest);
            return Results.Ok(pagamentos.Checkout(pedido));
        }).AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDaConta);

        grupo.MapPost("/webhook", async (HttpContext http, IOptions<OpcoesDePagamento> opcoes, IPagamentos pagamentos, TimeProvider relogio, CancellationToken ct) =>
        {
            using var leitor = new StreamReader(http.Request.Body);
            var corpo = await leitor.ReadToEndAsync(ct);
            var cfg = opcoes.Value;
            if (!RegrasDoGateway.Valida(http.Request.Headers[RegrasDoGateway.Cabecalho].ToString(), cfg.SegredoDoWebhook, corpo, relogio.GetUtcNow(), cfg.ToleranciaSegundos))
                return Results.Problem("Assinatura do aviso inválida.", statusCode: StatusCodes.Status401Unauthorized);

            AvisoDoGatewayV1? aviso;
            try
            {
                aviso = JsonSerializer.Deserialize<AvisoDoGatewayV1>(corpo, Json);
            }
            catch (JsonException)
            {
                return Results.Problem("O corpo não é um aviso válido.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (aviso is null || string.IsNullOrWhiteSpace(aviso.Id) || aviso.Id.Length > 100 || string.IsNullOrWhiteSpace(aviso.Tipo))
                return Results.Problem("O aviso precisa do id e do tipo.", statusCode: StatusCodes.Status400BadRequest);

            // 200 nos dois casos: o gateway reenvia o que não teve 2xx, e o repetido já foi feito.
            var novo = await pagamentos.ProcessarAsync(aviso, ct);
            return Results.Ok(new { processado = novo });
        }).AllowAnonymous();
    }

    /// <summary>A política de login que vale para o pedido: a da empresa do token, ou a da máquina da chave, ou a global.</summary>
    public static async Task<bool> LoginObrigatorioAsync(HttpContext http, ClaimsPrincipal quem, IMaquinas maquinas, IContas contas, CancellationToken ct)
    {
        int? empresa = int.TryParse(quem.FindFirstValue(Seguranca.ClaimEmpresa), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var daClaim)
            ? daClaim
            : null;
        if (empresa is null && http.Request.Headers.TryGetValue(ContratoV1.CabecalhoDaChave, out var chave) && await maquinas.AutenticarAsync(chave.ToString(), ct) is { } maquina)
            empresa = maquina.EmpresaId;
        return await contas.LoginObrigatorioAsync(empresa, ct);
    }

    /// <summary>Copia o pacote para a resposta em streaming, com os cabeçalhos NOSSOS — nada do GitHub passa adiante.</summary>
    public static async Task Transmitir(HttpContext http, PacoteAberto pacote, string arquivo, CancellationToken ct)
    {
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "application/zip";
        http.Response.Headers.ContentDisposition = $"attachment; filename={arquivo}";
        if (pacote.Tamanho is { } tamanho)
            http.Response.ContentLength = tamanho;
        await pacote.Conteudo.CopyToAsync(http.Response.Body, ct);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A última release do catálogo com o manifesto válido; nula quando indisponível.</summary>
    private static async Task<(ReleaseDoCatalogo Release, ManifestoDoCatalogoV1 Manifesto)?> UltimoAsync(IReleasesDoDevKit releases, CancellationToken ct)
    {
        if (await releases.CatalogosAsync(ct) is not [var ultima, ..])
            return null;
        return await releases.ManifestoAsync(ultima, ct) is { } manifesto ? (ultima, manifesto) : null;
    }

    /// <summary>
    /// Os nomes do catálogo básico (<c>tipo:nome</c>) para a regra da colisão. Sem o catálogo (GitHub fora do ar), a
    /// lista é vazia: a publicação segue, e a máquina continua preferindo o básico na sincronia.
    /// </summary>
    private static async Task<IReadOnlySet<string>> NomesDoBasicoAsync(IReleasesDoDevKit releases, CancellationToken ct)
        => await UltimoAsync(releases, ct) is { } ultimo
            ? ultimo.Manifesto.Itens.Select(i => RegrasDoCatalogoDaEmpresa.Chave(i.Tipo, i.Nome)).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static IResult CatalogoIndisponivel()
        => Results.Problem("O catálogo do dev.kit está indisponível no momento. Tente de novo em alguns minutos.", statusCode: StatusCodes.Status503ServiceUnavailable);

    private static Task<IResult> ComEmpresa(ClaimsPrincipal quem, Func<EscopoDaEmpresa, Task<IResult>> acao)
        => Seguranca.EscopoDaEmpresa(quem) is { } escopo ? acao(escopo) : Task.FromResult(Results.Forbid());

    private static Task<IResult> ComGestor(ClaimsPrincipal quem, Func<EscopoDaEmpresa, Task<IResult>> acao)
        => Seguranca.EscopoDaEmpresa(quem) is { EhGestor: true } escopo ? acao(escopo) : Task.FromResult(Results.Forbid());

    private static IResult Resultado<T>(ResultadoDaGestao<T> resultado) where T : class
        => resultado.Valor is { } valor ? Results.Ok(valor) : Falha(resultado);

    /// <summary>A falha no status HTTP: 404, 409, 403 ou 400, sempre com a mensagem.</summary>
    private static IResult Falha<T>(ResultadoDaGestao<T> resultado) where T : class
        => Results.Problem(resultado.Mensagem, statusCode: resultado.Falha switch
        {
            FalhaNaGestao.NaoEncontrado => StatusCodes.Status404NotFound,
            FalhaNaGestao.Conflito => StatusCodes.Status409Conflict,
            FalhaNaGestao.Proibida => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        });
}
