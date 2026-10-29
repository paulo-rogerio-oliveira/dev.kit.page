using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

public sealed class AssinantesDoCatalogoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static async Task<long> Publicar(HttpClient cliente, string nome, string tipo = "skill", string[]? deps = null)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/catalogo/empresa", new PublicacaoV1(tipo, nome, "Demonstração", "{}", deps));
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PublicacaoFeitaV1>())!.Id;
    }

    [Fact]
    public async Task Consulta_nominal_exige_gestor_e_isola_a_empresa_do_token()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var outro = await _api.GestorAsync(admin, b.Id, "gestor.b");
        var (dev, _) = await _api.UsuarioAsync(admin, "dev.a", Papeis.Dev, a.Id);
        var id = await Publicar(dev, "skill-demo");
        var url = $"/api/catalogo/empresa/{id}/assinantes";
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outro.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestor.GetAsync("/api/catalogo/empresa/999999/assinantes")).StatusCode);
        var vazio = (await gestor.GetFromJsonAsync<Pagina<AssinanteDoItemV1>>(url))!;
        Assert.Empty(vazio.Itens);
        Assert.Equal(0, vazio.Total);
    }

    [Fact]
    public async Task Consulta_inclui_dependencias_preserva_retirada_e_reflete_desistencia_sem_expor_credenciais()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var gestor = await _api.GestorAsync(admin, empresa.Id, "gestor.a");
        var (ana, _) = await _api.UsuarioAsync(admin, "ana.demo", Papeis.Dev, empresa.Id);
        var (bia, _) = await _api.UsuarioAsync(admin, "bia.demo", Papeis.Dev, empresa.Id);
        var skill = await Publicar(ana, "skill-demo");
        var fluxo = await Publicar(ana, "fluxo-demo", "fluxo", ["skill:skill-demo"]);
        (await bia.PostAsync($"/api/catalogo/empresa/{fluxo}/assinatura", null)).EnsureSuccessStatusCode();
        (await ana.PostAsync($"/api/catalogo/empresa/{skill}/assinatura", null)).EnsureSuccessStatusCode();
        var url = $"/api/catalogo/empresa/{skill}/assinantes";
        var lista = (await gestor.GetFromJsonAsync<Pagina<AssinanteDoItemV1>>(url))!;
        Assert.Equal(2, lista.Total);
        Assert.Equal(new[] { "Nome de ana.demo", "Nome de bia.demo" }, lista.Itens.Select(i => i.Nome));
        Assert.All(lista.Itens, i => Assert.Equal(TimeSpan.Zero, i.AssinadaEm.Offset));
        using var json = JsonDocument.Parse(await gestor.GetStringAsync(url));
        Assert.Equal(new[] { "assinadaEm", "nome", "usuarioId" }, json.RootElement.GetProperty("itens")[0].EnumerateObject().Select(p => p.Name).Order());
        (await gestor.DeleteAsync($"/api/catalogo/empresa/{skill}")).EnsureSuccessStatusCode();
        var retirado = (await gestor.GetFromJsonAsync<Pagina<AssinanteDoItemV1>>(url))!;
        Assert.Equal(lista.Total, retirado.Total);
        Assert.Equal(lista.Itens, retirado.Itens);
        (await bia.DeleteAsync($"/api/catalogo/empresa/{skill}/assinatura")).EnsureSuccessStatusCode();
        var depois = (await gestor.GetFromJsonAsync<Pagina<AssinanteDoItemV1>>(url))!;
        Assert.Equal(1, depois.Total);
        Assert.Equal("Nome de ana.demo", Assert.Single(depois.Itens).Nome);
    }

    [Fact]
    public async Task Paginacao_limita_tamanho_ordena_com_desempate_e_nao_estoura_com_inteiros_extremos()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var gestor = await _api.GestorAsync(admin, empresa.Id, "gestor.a");
        var (bia, _) = await _api.UsuarioAsync(admin, "bia", Papeis.Dev, empresa.Id);
        var (ana, _) = await _api.UsuarioAsync(admin, "ana", Papeis.Dev, empresa.Id);
        var id = await Publicar(gestor, "skill-demo");
        foreach (var cliente in new[] { bia, ana, gestor })
            (await cliente.PostAsync($"/api/catalogo/empresa/{id}/assinatura", null)).EnsureSuccessStatusCode();
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
        await db.Usuarios.Where(u => u.EmpresaId == empresa.Id && u.Papel == Papeis.Dev).ExecuteUpdateAsync(s => s.SetProperty(u => u.Nome, "Pessoa Demonstração"));
        var url = $"/api/catalogo/empresa/{id}/assinantes";
        var todos = (await gestor.GetFromJsonAsync<Pagina<AssinanteDoItemV1>>(url + "?tamanho=999"))!;
        Assert.Equal(100, todos.Tamanho);
        Assert.Equal(3, todos.Total);
        Assert.Equal(3, todos.Itens.Count);
        var registros = await db.AssinaturasDeCatalogo.Where(a => a.ItemId == id).ToListAsync();
        Assert.Equal(registros.Count, todos.Total);
        Assert.All(todos.Itens, i => Assert.Contains(registros, a => a.UsuarioId == i.UsuarioId && DevKitPageDb.Utc(a.AssinadaEmUtc) == i.AssinadaEm));
        Assert.Equal(todos.Itens.Where(i => i.Nome == "Pessoa Demonstração").Select(i => i.UsuarioId).Order(), todos.Itens.Where(i => i.Nome == "Pessoa Demonstração").Select(i => i.UsuarioId));
        Assert.Contains(todos.Itens, i => i.Nome == $"Usuário {i.UsuarioId}");
        for (var pagina = 1; pagina <= 3; pagina++)
        {
            var parte = (await gestor.GetFromJsonAsync<Pagina<AssinanteDoItemV1>>(url + $"?pagina={pagina}&tamanho=1"))!;
            Assert.Equal(todos.Itens[pagina - 1], Assert.Single(parte.Itens));
        }
        var minimo = (await gestor.GetFromJsonAsync<Pagina<AssinanteDoItemV1>>(url + "?pagina=-1&tamanho=0"))!;
        Assert.Equal(1, minimo.NumeroDaPagina);
        Assert.Equal(1, minimo.Tamanho);
        var extremo = (await gestor.GetFromJsonAsync<Pagina<AssinanteDoItemV1>>(url + "?pagina=2147483647&tamanho=2147483647"))!;
        Assert.Empty(extremo.Itens);
        Assert.Equal(3, extremo.Total);
    }

}
