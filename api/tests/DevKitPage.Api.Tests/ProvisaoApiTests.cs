using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A provisão pela API (<c>/api/provisao</c>): sem a liberação, TODA rota de recurso recusa (403 com o código) — a ação
/// escondida no site não é a proteção; o admin libera e retira por usuário, e a retirada vale no pedido seguinte; o pedido é
/// assíncrono (202) e repetível (o segundo devolve a mesma fila, 200); a região fora da lista é 400; o recurso de outro
/// usuário é 404; e o dev não libera para si mesmo.
/// </summary>
public sealed class ProvisaoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<(HttpClient Admin, HttpClient Dev, int DevId)> AdminEDevAsync(string login = "ana")
    {
        var admin = await _api.AdminAsync();
        var (dev, _) = await _api.UsuarioAsync(admin, login, Papeis.Dev);
        var id = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == login).Id;
        return (admin, dev, id);
    }

    private static Task<HttpResponseMessage> LiberarAsync(HttpClient admin, int id, bool liberado = true, string recurso = ProvisaoV1.RecursoProvisao)
        => admin.PutAsJsonAsync($"/api/usuarios/{id}/recursos/{recurso}", new LiberacaoDeRecursoV1(liberado));

    private static async Task<string?> CodigoAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null;
    }

    [Fact]
    public async Task Sem_login_tudo_e_401()
    {
        var anonimo = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/provisao/permissao")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/provisao/recursos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/provisao/filas", new PedidoDeFilaV1(null))).StatusCode);
    }

    [Fact]
    public async Task Sem_a_liberacao_a_permissao_diz_nao_e_toda_rota_de_recurso_recusa_com_o_codigo()
    {
        var (_, dev, _) = await AdminEDevAsync();

        var permissao = (await dev.GetFromJsonAsync<PermissaoDeProvisaoV1>("/api/provisao/permissao"))!;
        Assert.Equal((false, ProvisaoV1.OrigemNenhuma), (permissao.Liberada, permissao.Origem));

        foreach (var resposta in new[]
                 {
                     await dev.GetAsync("/api/provisao/recursos"),
                     await dev.GetAsync("/api/provisao/recursos/1"),
                     await dev.PostAsJsonAsync("/api/provisao/filas", new PedidoDeFilaV1("brazilsouth")),
                     await dev.PostAsync("/api/provisao/recursos/1/repetir", null),
                     await dev.DeleteAsync("/api/provisao/recursos/1"),
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
            Assert.Equal(ProvisaoV1.CodigoNaoLiberada, await CodigoAsync(resposta));
        }
    }

    [Fact]
    public async Task Liberada_o_pedido_e_aceito_202_e_o_repetido_devolve_a_mesma_fila_200()
    {
        var (admin, dev, id) = await AdminEDevAsync();
        Assert.Equal(HttpStatusCode.OK, (await LiberarAsync(admin, id)).StatusCode);

        var permissao = (await dev.GetFromJsonAsync<PermissaoDeProvisaoV1>("/api/provisao/permissao"))!;
        Assert.Equal((true, ProvisaoV1.OrigemPermissao), (permissao.Liberada, permissao.Origem));
        Assert.Contains(permissao.RegiaoPadrao, permissao.Regioes);

        var pedido = await dev.PostAsJsonAsync("/api/provisao/filas", new PedidoDeFilaV1(null));
        Assert.Equal(HttpStatusCode.Accepted, pedido.StatusCode);
        var recurso = (await pedido.Content.ReadFromJsonAsync<RecursoProvisionadoV1>())!;
        Assert.Equal((ProvisaoV1.TipoFila, ProvisaoV1.EstadoPedido, permissao.RegiaoPadrao), (recurso.Tipo, recurso.Estado, recurso.Regiao));
        Assert.Equal($"/api/provisao/recursos/{recurso.Id}", pedido.Headers.Location!.OriginalString);

        var repetido = await dev.PostAsJsonAsync("/api/provisao/filas", new PedidoDeFilaV1("eastus2"));
        Assert.Equal(HttpStatusCode.OK, repetido.StatusCode);
        Assert.Equal(recurso.Id, (await repetido.Content.ReadFromJsonAsync<RecursoProvisionadoV1>())!.Id);

        var lista = (await dev.GetFromJsonAsync<RecursoProvisionadoV1[]>("/api/provisao/recursos"))!;
        Assert.Equal([recurso.Id], lista.Select(r => r.Id));
        var detalhe = (await dev.GetFromJsonAsync<DetalheDoRecursoV1>($"/api/provisao/recursos/{recurso.Id}"))!;
        var auditoria = Assert.Single(detalhe.Eventos);
        Assert.Equal((ProvisaoV1.EventoPedido, "ana"), (auditoria.Evento, auditoria.Autor));
    }

    [Fact]
    public async Task A_regiao_fora_da_lista_e_400_e_nada_e_gravado()
    {
        var (admin, dev, id) = await AdminEDevAsync();
        await LiberarAsync(admin, id);

        var resposta = await dev.PostAsJsonAsync("/api/provisao/filas", new PedidoDeFilaV1("westeurope"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains("regiao", await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty((await dev.GetFromJsonAsync<RecursoProvisionadoV1[]>("/api/provisao/recursos"))!);
    }

    [Fact]
    public async Task O_recurso_de_outro_usuario_e_404_e_a_remocao_em_criacao_e_409()
    {
        var (admin, ana, anaId) = await AdminEDevAsync("ana");
        var (bia, _) = await _api.UsuarioAsync(admin, "bia", Papeis.Dev);
        var biaId = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == "bia").Id;
        await LiberarAsync(admin, anaId);
        await LiberarAsync(admin, biaId);
        var recurso = (await (await ana.PostAsJsonAsync("/api/provisao/filas", new PedidoDeFilaV1(null))).Content.ReadFromJsonAsync<RecursoProvisionadoV1>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync($"/api/provisao/recursos/{recurso.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.DeleteAsync($"/api/provisao/recursos/{recurso.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PostAsync($"/api/provisao/recursos/{recurso.Id}/repetir", null)).StatusCode);
        Assert.Empty((await bia.GetFromJsonAsync<RecursoProvisionadoV1[]>("/api/provisao/recursos"))!);

        // A criação ainda não terminou: remove-se depois; e só o que falhou se repete.
        Assert.Equal(HttpStatusCode.Conflict, (await ana.DeleteAsync($"/api/provisao/recursos/{recurso.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsync($"/api/provisao/recursos/{recurso.Id}/repetir", null)).StatusCode);
    }

    [Fact]
    public async Task A_retirada_vale_no_pedido_seguinte_sem_esperar_o_token_vencer()
    {
        var (admin, dev, id) = await AdminEDevAsync();
        await LiberarAsync(admin, id);
        Assert.Equal(HttpStatusCode.OK, (await dev.GetAsync("/api/provisao/recursos")).StatusCode);

        var retirada = await LiberarAsync(admin, id, liberado: false);
        Assert.Empty((await retirada.Content.ReadFromJsonAsync<UsuarioResumo>())!.RecursosLiberados!);

        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/provisao/recursos")).StatusCode);
        Assert.False((await dev.GetFromJsonAsync<PermissaoDeProvisaoV1>("/api/provisao/permissao"))!.Liberada);
    }

    [Fact]
    public async Task So_o_admin_libera_o_recurso_desconhecido_e_400_e_a_lista_mostra_o_liberado()
    {
        var (admin, dev, id) = await AdminEDevAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await LiberarAsync(dev, id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LiberarAsync(admin, id, recurso: "app-service")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await LiberarAsync(admin, 9999)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/usuarios/{id}/recursos/provisao", JsonContent.Create<LiberacaoDeRecursoV1?>(null))).StatusCode);

        var liberado = (await (await LiberarAsync(admin, id)).Content.ReadFromJsonAsync<UsuarioResumo>())!;
        Assert.Equal([ProvisaoV1.RecursoProvisao], liberado.RecursosLiberados);
        var lista = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!;
        Assert.Equal([ProvisaoV1.RecursoProvisao], lista.Single(u => u.Id == id).RecursosLiberados);
        Assert.Empty(lista.Single(u => u.Login == "admin").RecursosLiberados!);
    }

    [Fact]
    public async Task O_admin_que_ainda_nao_trocou_a_senha_nao_libera()
    {
        var pendente = _api.CreateClient();
        pendente.DefaultRequestHeaders.Authorization = new("Bearer", (await _api.LoginAsync(pendente)).Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await LiberarAsync(pendente, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pendente.GetAsync("/api/provisao/permissao")).StatusCode);
    }
}
