using DevKitPage.Api;
using DevKitPage.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AdicionarInfraestrutura(builder.Configuration);
builder.Services.AdicionarSeguranca(builder.Environment);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<DevKitPageDb>("banco");
builder.Services.AddHostedService<ExpurgoDiario>();

// CORS por configuração: em Development, o Vite; no Azure, a origem do Static Web App.
builder.Services.AddCors(opcoes => opcoes.AddDefaultPolicy(politica =>
    politica.WithOrigins(builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? Array.Empty<string>())
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

Seguranca.ValidarSegredo(app.Services, app.Environment);
using (var escopo = app.Services.CreateScope())
    await escopo.ServiceProvider.GetRequiredService<InicializadorDaBase>().InicializarAsync();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapearAutenticacao();
app.MapearMaquinas();
app.MapearTelemetria();
app.MapearPainel();

await app.RunAsync();

/// <summary>O ponto de entrada — público para o <c>WebApplicationFactory</c> dos testes.</summary>
public partial class Program;
