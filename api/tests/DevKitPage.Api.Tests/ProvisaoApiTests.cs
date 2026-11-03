using System.Net;
using System.Net.Http.Json;
using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A provisão pela API (projeto controle-remoto, item 4): a permissão por usuário (a ação escondida e a API recusando), o
/// isolamento entre usuários, os códigos de cada rota, a gestão da liberação pelo admin e a API pública SEM o motor nem a
/// identidade do Azure.
/// </summary>
public sealed class ProvisaoApiTests : IDisposable
{
    private const string Situacao = ProvisaoV1.Rotas;
    private const string Filas = ProvisaoV1.Rotas + "/filas";
    private const string Liberacoes = ProvisaoV1.Rotas + "/liberacoes";

    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static string Recurso(long id) => $"{ProvisaoV1.Rotas}/recursos/{id}";

    private async Task<(HttpClient Admin, HttpClient Dev, int DevId)> DevAsync(string login = "ana.dev", bool liberar = false)
    {
        var admin = await _api.AdminAsync();
        var (dev, _) = await _api.UsuarioAsync(admin, login, Papeis.Dev);
        var id = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == login).Id;
        if (liberar)
            (await admin.PutAsJsonAsync($"{Liberacoes}/{id}", new DefinicaoDaLiberacaoV1(true))).EnsureSuccessStatusCode();
        return (admin, dev, id);
    }

    private static async Task<RecursoNaNuvemV1> PedirAsync(HttpClient cliente, string regiao = "brazilsouth")
    {
        var resposta = await cliente.PostAsJsonAsync(Filas, new PedidoDeFilaV1(regiao));
        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<RecursoNaNuvemV1>())!;
    }

    [Fact]
    public async Task Sem_token_toda_rota_e_401()
    {
        var anonimo = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Situacao)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync(Filas, new PedidoDeFilaV1("brazilsouth"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Recurso(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsync($"{Recurso(1)}/repetir", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.DeleteAsync(Recurso(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Liberacoes)).StatusCode);
    }

    [Fact]
    public async Task Sem_a_liberacao_a_situacao_diz_nao_e_o_pedido_e_403_sem_gravar()
    {
        var (_, dev, _) = await DevAsync();

        var situacao = (await dev.GetFromJsonAsync<SituacaoDaProvisaoV1>(Situacao))!;
        Assert.False(situacao.Liberada);
        Assert.Null(situacao.Origem);
        Assert.Empty(situacao.Recursos);

        var pedido = await dev.PostAsJsonAsync(Filas, new PedidoDeFilaV1("brazilsouth"));
        Assert.Equal(HttpStatusCode.Forbidden, pedido.StatusCode);
        Assert.Contains("não está liberada", await pedido.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // Nem o corpo inválido passa da permissão.
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.PostAsJsonAsync(Filas, new PedidoDeFilaV1("westeurope"))).StatusCode);
        Assert.Empty((await dev.GetFromJsonAsync<SituacaoDaProvisaoV1>(Situacao))!.Recursos);
    }

    [Fact]
    public async Task Com_a_liberacao_o_pedido_e_aceito_e_o_repetido_devolve_o_mesmo()
    {
        var (_, dev, devId) = await DevAsync(liberar: true);

        var situacao = (await dev.GetFromJsonAsync<SituacaoDaProvisaoV1>(Situacao))!;
        Assert.Equal((true, ProvisaoV1.OrigemUsuario, "brazilsouth"), (situacao.Liberada, situacao.Origem, situacao.RegiaoPadrao));
        Assert.Contains("eastus2", situacao.Regioes);

        var resposta = await dev.PostAsJsonAsync(Filas, new PedidoDeFilaV1("brazilsouth"));
        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        var fila = (await resposta.Content.ReadFromJsonAsync<RecursoNaNuvemV1>())!;
        Assert.Equal(Recurso(fila.Id), resposta.Headers.Location!.OriginalString);
        Assert.Equal((ProvisaoV1.Pedido, $"rg-devkit-u{devId}", "comandos"), (fila.Estado, fila.GrupoDeRecursos, fila.Fila));
        Assert.True(RegrasDaProvisao.NomeDaContaValido(fila.Conta));

        var repetido = await dev.PostAsJsonAsync(Filas, new PedidoDeFilaV1("eastus2"));
        Assert.Equal(HttpStatusCode.OK, repetido.StatusCode);
        Assert.Equal(fila.Id, (await repetido.Content.ReadFromJsonAsync<RecursoNaNuvemV1>())!.Id);

        var lido = (await dev.GetFromJsonAsync<RecursoNaNuvemV1>(Recurso(fila.Id)))!;
        Assert.Equal(ProvisaoV1.Pedido, Assert.Single(lido.Historico!).Estado);
    }

    [Fact]
    public async Task O_pedido_fora_do_contrato_e_400()
    {
        var (_, dev, _) = await DevAsync(liberar: true);

        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PostAsJsonAsync(Filas, new PedidoDeFilaV1("westeurope"))).StatusCode);
        // Os nomes saem do servidor: o corpo não escolhe o grupo nem a conta.
        var comNome = new StringContent("""{"regiao":"brazilsouth","conta":"minhaconta"}""", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PostAsync(Filas, comNome)).StatusCode);
        Assert.Empty((await dev.GetFromJsonAsync<SituacaoDaProvisaoV1>(Situacao))!.Recursos);
    }

    [Fact]
    public async Task O_recurso_de_outro_usuario_e_404_para_o_colega_o_gestor_e_o_admin()
    {
        var (admin, dev, _) = await DevAsync(liberar: true);
        var fila = await PedirAsync(dev);
        var (colega, _) = await _api.UsuarioAsync(admin, "bruno.dev", Papeis.Dev);
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var gestor = await _api.GestorAsync(admin, empresa.Id, "gestor.a");

        foreach (var outro in new[] { colega, gestor, admin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync(Recurso(fila.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outro.DeleteAsync(Recurso(fila.Id))).StatusCode);
            Assert.DoesNotContain((await outro.GetFromJsonAsync<SituacaoDaProvisaoV1>(Situacao))!.Recursos, r => r.Id == fila.Id);
        }

        Assert.Equal(ProvisaoV1.Pedido, (await dev.GetFromJsonAsync<RecursoNaNuvemV1>(Recurso(fila.Id)))!.Estado);
    }

    [Fact]
    public async Task Remover_durante_a_criacao_e_409_e_repetir_o_que_nao_falhou_tambem()
    {
        var (_, dev, _) = await DevAsync(liberar: true);
        var fila = await PedirAsync(dev);

        Assert.Equal(HttpStatusCode.Conflict, (await dev.DeleteAsync(Recurso(fila.Id))).StatusCode);
        // Pedido ainda andando: o repetir é aceito sem mudar nada (o clique em dobro).
        Assert.Equal(HttpStatusCode.Accepted, (await dev.PostAsync($"{Recurso(fila.Id)}/repetir", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dev.PostAsync($"{Recurso(9999)}/repetir", null)).StatusCode);
    }

    [Fact]
    public async Task A_fila_pronta_e_removida_pelo_dono_mesmo_depois_de_revogada_a_liberacao()
    {
        var (admin, dev, devId) = await DevAsync(liberar: true);
        var fila = await PedirAsync(dev);
        await MarcarAsync(fila.Id, ProvisaoV1.Pronto);
        (await admin.PutAsJsonAsync($"{Liberacoes}/{devId}", new DefinicaoDaLiberacaoV1(false))).EnsureSuccessStatusCode();

        var situacao = (await dev.GetFromJsonAsync<SituacaoDaProvisaoV1>(Situacao))!;
        Assert.False(situacao.Liberada);
        Assert.Single(situacao.Recursos);

        var remocao = await dev.DeleteAsync(Recurso(fila.Id));
        Assert.Equal(HttpStatusCode.Accepted, remocao.StatusCode);
        var removendo = (await remocao.Content.ReadFromJsonAsync<RecursoNaNuvemV1>())!;
        Assert.Equal((ProvisaoV1.OperacaoRemover, ProvisaoV1.Pedido), (removendo.Operacao, removendo.Estado));
        // A criação de outra, sem a liberação, continua recusada.
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.PostAsJsonAsync(Filas, new PedidoDeFilaV1("brazilsouth"))).StatusCode);
    }

    [Fact]
    public async Task Repetir_a_criacao_que_falhou_exige_a_liberacao()
    {
        var (admin, dev, devId) = await DevAsync(liberar: true);
        var fila = await PedirAsync(dev);
        await MarcarAsync(fila.Id, ProvisaoV1.Falhou);
        (await admin.PutAsJsonAsync($"{Liberacoes}/{devId}", new DefinicaoDaLiberacaoV1(false))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await dev.PostAsync($"{Recurso(fila.Id)}/repetir", null)).StatusCode);

        (await admin.PutAsJsonAsync($"{Liberacoes}/{devId}", new DefinicaoDaLiberacaoV1(true))).EnsureSuccessStatusCode();
        var repetir = await dev.PostAsync($"{Recurso(fila.Id)}/repetir", null);
        Assert.Equal(HttpStatusCode.Accepted, repetir.StatusCode);
        Assert.Equal(ProvisaoV1.Pedido, (await repetir.Content.ReadFromJsonAsync<RecursoNaNuvemV1>())!.Estado);
    }

    [Fact]
    public async Task So_o_admin_libera_e_a_lista_guarda_quem_alterou()
    {
        var (admin, dev, devId) = await DevAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var gestor = await _api.GestorAsync(admin, empresa.Id, "gestor.b");

        foreach (var naoAdmin in new[] { dev, gestor })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await naoAdmin.GetAsync(Liberacoes)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await naoAdmin.PutAsJsonAsync($"{Liberacoes}/{devId}", new DefinicaoDaLiberacaoV1(true))).StatusCode);
        }

        var liberada = await admin.PutAsJsonAsync($"{Liberacoes}/{devId}", new DefinicaoDaLiberacaoV1(true));
        Assert.Equal(HttpStatusCode.OK, liberada.StatusCode);
        var linha = Assert.Single((await admin.GetFromJsonAsync<LiberacaoDaProvisaoV1[]>(Liberacoes))!);
        Assert.Equal((devId, "ana.dev", true, "admin"), (linha.UsuarioId, linha.Login, linha.Liberada, linha.AlteradaPor));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"{Liberacoes}/9999", new DefinicaoDaLiberacaoV1(true))).StatusCode);
        Assert.True((await dev.GetFromJsonAsync<SituacaoDaProvisaoV1>(Situacao))!.Liberada);
    }

    [Fact]
    public async Task Os_pedidos_tem_limite_por_usuario()
    {
        var (_, dev, _) = await DevAsync();
        var limite = _api.Services.GetRequiredService<IOptions<OpcoesDaProvisao>>().Value.PedidosPorMinuto;

        var status = new List<HttpStatusCode>();
        for (var i = 0; i <= limite; i++)
            status.Add((await dev.PostAsJsonAsync(Filas, new PedidoDeFilaV1("brazilsouth"))).StatusCode);

        Assert.All(status.Take(limite), s => Assert.Equal(HttpStatusCode.Forbidden, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, status[^1]);
    }

    [Fact]
    public void A_api_publica_nao_carrega_o_motor_nem_a_identidade_do_azure()
    {
        var referencias = typeof(EndpointsDaProvisao).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain("Azure.Identity", referencias);
        Assert.DoesNotContain("DevKitPage.Provisao", referencias);

        using var escopo = _api.Services.CreateScope();
        Assert.Null(escopo.ServiceProvider.GetService<IMotorDeProvisao>());
        Assert.Null(escopo.ServiceProvider.GetService<IProvedorDeNuvem>());
        Assert.NotNull(escopo.ServiceProvider.GetService<IProvisao>());
    }

    /// <summary>O desfecho do trabalhador, gravado direto: a API não tem o motor, e o teste dele é o da Infrastructure.</summary>
    private async Task MarcarAsync(long id, string estado)
    {
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<DevKitPage.Infrastructure.DevKitPageDb>();
        await db.RecursosNaNuvem.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Estado, estado));
    }
}
