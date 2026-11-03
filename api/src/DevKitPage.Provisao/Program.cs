using DevKitPage.Provisao;
using Microsoft.Extensions.Hosting;

// O worker da provisão: um processo à parte da API pública, com a identidade gerenciada própria. Lê os pedidos da mesma base
// (ConnectionStrings:DevKitPage e Banco:Provider, como a API) e os executa no Azure. Ver docs/publicacao-azure.md.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AdicionarWorkerDaProvisao(builder.Configuration, builder.Environment);
await builder.Build().RunAsync();
