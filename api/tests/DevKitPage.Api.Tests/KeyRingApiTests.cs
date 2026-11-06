using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

/// <summary>O key ring do DataProtection na subida da API: Production exige o persistido; com ele, a chave vai para onde foi dito.</summary>
public sealed class KeyRingApiTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"devkitpage-keyring-api-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_api_nao_sobe_em_production_sem_o_key_ring_persistido()
    {
        using var api = new ApiDeTeste(ambiente: "Production");

        var erro = Assert.ThrowsAny<Exception>(() => api.CreateClient());

        Assert.Contains("ProtecaoDeDados não configurada", erro.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Em_production_com_o_key_ring_configurado_a_api_sobe_e_grava_a_chave_nele()
    {
        using var api = new ApiDeTeste(ambiente: "Production");
        using var configurada = api.WithWebHostBuilder(b => b.UseSetting("ProtecaoDeDados:Pasta", _pasta));

        var saude = await configurada.CreateClient().GetAsync("/health");
        configurada.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("teste").Protect("x");

        Assert.True(saude.IsSuccessStatusCode);
        Assert.Single(Directory.GetFiles(_pasta, "key-*.xml"));
    }
}
