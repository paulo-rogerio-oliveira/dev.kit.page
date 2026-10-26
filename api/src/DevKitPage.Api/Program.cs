using DevKitPage.Api;
using DevKitPage.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AdicionarInfraestrutura(builder.Configuration);
builder.Services.AdicionarSeguranca(builder.Environment);
builder.Services.AdicionarLimiteDeTaxa(builder.Configuration);
builder.Services.AddProblemDetails();
// O 500 com a rota, a causa e o código do log, no lugar do "An error occurred while processing your request".
builder.Services.AddExceptionHandler<ErroUtilDaApi>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<DevKitPageDb>("banco").AddCheck<EsquemaDoBancoCheck>("esquema");
builder.Services.AddHostedService<ExpurgoDiario>();

// CORS por configuração: em Development, o Vite; no Azure, a origem do Static Web App.
builder.Services.AddCors(opcoes => opcoes.AddDefaultPolicy(politica =>
    politica.WithOrigins(builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? Array.Empty<string>())
        .AllowAnyHeader()
        .AllowAnyMethod()
        // O nome do arquivo das exportações (US #381): sem isto o navegador de outra origem não o lê.
        .WithExposedHeaders("Content-Disposition")));

var app = builder.Build();

Seguranca.ValidarSegredo(app.Services, app.Environment);
using (var escopo = app.Services.CreateScope())
    await escopo.ServiceProvider.GetRequiredService<InicializadorDaBase>().InicializarAsync();

// O IP do cliente atrás do proxy ANTES de tudo que o usa (o limite de taxa do formulário).
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapearAutenticacao();
app.MapearMaquinas();
app.MapearTelemetria();
app.MapearPainel();
app.MapearPainelDeUtilidade();
app.MapearEmpresas();
app.MapearUsuarios();
app.MapearVersoes();
app.MapearDemonstracoes();
app.MapearCatalogoBase();
app.MapearCatalogoDaEmpresa();
app.MapearCatalogos();
app.MapearEdicaoDeArtefato();
app.MapearConta();
app.MapearConhecimentoDaEmpresa();
app.MapearContextoPorNivel();
app.MapearOrigemDoContexto();
app.MapearRelatos();
app.MapearDesign();
app.MapearLicencas();
app.MapearLicencaDosUsuarios();
app.MapearEdicaoDeEmpresas();
app.MapearPagamentos();

await app.RunAsync();

/// <summary>O ponto de entrada — público para o <c>WebApplicationFactory</c> dos testes.</summary>
public partial class Program;
