using DevKitPage.Infrastructure;
using DevKitPage.Provisao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// O trabalhador da provisão: o mesmo banco da API, o motor da Infrastructure e o provedor do Azure com a identidade dele.
// Não aplica migration nem semeia nada: quem cuida do esquema é a API (SQLite) ou o script (Azure SQL).
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AdicionarInfraestrutura(builder.Configuration);
builder.Services.AdicionarMotorDeProvisao();
builder.Services.AdicionarNuvem(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<TrabalhadorDaProvisao>();

await builder.Build().RunAsync();

/// <summary>O ponto de entrada do trabalhador.</summary>
public partial class Program;
