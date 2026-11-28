using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKit.Matrix;
using DevKitPage.Contracts.V1;
using DevKitPage.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

public sealed partial class ConversaApiTests
{
    private static EnvioDeProjetoV1 ProjetoParaDisparo(string situacao = "aprovado") => new("disparo", "Disparo", "usuario", "",
        ["repo-a"], "feature/disparo", false, "Garantir matriz", [], true, 1, "main", situacao, null,
        [new(1, "Implementar", "Pedido", "", [], [], [], 1, 0, "planejado")]);

    /// <summary>(boards-estados, item 5) A situação em que cada ação remota vale: aprovar no aguardando, retomar no pausado.</summary>
    private static string SituacaoDaAcao(string acao) => acao switch { "retomar" => "pausado", "aprovar" => "aguardando", _ => "aprovado" };

    private static DocumentoMatrix MapaDoDisparo() => new() { Aplicacao = "disparo", Id = "disparo",
        Mapa = new("App", ["repo-a"], [new("disparo", "Disparo")], [new("servico", "Serviço")], []) };
    private static DocumentoMatrix MatrizDoDisparo() => new() { Aplicacao = "disparo", Id = "disparo", Tipo = "matriz", Projeto = "disparo",
        Matriz = new(1, [new("disparo", "Disparo")], [new("servico", "Serviço")],
            [new("c1", "disparo", "servico", "alterar", true, "Pedido", "Plano", [new("item-1", 1)], "Bloquear", "Zero efeitos")]) };

    private static async Task DefinirDisparoAsync(HttpClient cliente, DocumentoMatrix? matriz = null)
    {
        (await cliente.PutAsJsonAsync("/api/matrix/disparo/mapa/disparo?aplicar=true", new EdicaoMatrix(0, MapaDoDisparo()))).EnsureSuccessStatusCode();
        (await cliente.PutAsJsonAsync("/api/matrix/disparo/matriz/disparo?aplicar=true", new EdicaoMatrix(0, matriz ?? MatrizDoDisparo()))).EnsureSuccessStatusCode();
    }

    private static ComandoNovoV1 PedidoDeDisparo(string acao = "disparar", int? item = null, string id = "disparo-1")
        => new(id, Maquina, ConversaV1.ComandoProjeto, JsonSerializer.Serialize(new { acao, projeto = "disparo", item }));

    [Theory]
    [InlineData("ausente", "disparar")]
    [InlineData("ausente", "retomar")]
    [InlineData("ausente", "aprovar")]
    [InlineData("vazia", "disparar")]
    [InlineData("vazia", "aprovar")]
    [InlineData("dono", "disparar")]
    [InlineData("dono", "aprovar")]
    [InlineData("mapa", "disparar")]
    [InlineData("coordenada", "disparar")]
    [InlineData("novo-item", "disparar")]
    [InlineData("novo-item", "aprovar")]
    public async Task Disparo_remoto_negativo_nao_grava_comando_nem_envia_fila(string caso, string acao)
    {
        var (_, dev) = await DevComFilaAsync();
        var envio = ProjetoParaDisparo(SituacaoDaAcao(acao));
        var projeto = (await (await dev.PutAsJsonAsync("/api/projetos", envio)).Content.ReadFromJsonAsync<ProjetoV1>())!;
        if (caso != "ausente")
        {
            var matriz = MatrizDoDisparo();
            if (caso == "vazia") matriz = matriz with { Matriz = matriz.Matriz! with { Celulas = [] } };
            if (caso == "dono") matriz = matriz with { Matriz = matriz.Matriz! with
                { Celulas = [matriz.Matriz!.Celulas[0] with { Responsaveis = [new("sem-item")] }] } };
            if (caso == "coordenada") matriz = matriz with { Matriz = matriz.Matriz! with
                { Layers = [new("servico", "Serviço"), new("cli", "CLI")] } };
            await DefinirDisparoAsync(dev, matriz);
            if (caso == "mapa")
            {
                using var escopo = _api.Services.CreateScope();
                var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
                var linha = db.Set<DevKitPage.Core.RevisaoMatrixNoBanco>().Single(r => r.Tipo == "matriz");
                linha.Documento = JsonSerializer.Serialize(matriz with { Matriz = matriz.Matriz! with { RevisaoDoMapa = 9 } }, Json);
                await db.SaveChangesAsync();
            }
            if (caso == "novo-item")
                (await dev.PutAsJsonAsync("/api/projetos", envio with { Versao = projeto.Versao,
                    Itens = [.. envio.Itens, envio.Itens[0] with { Numero = 2, Titulo = "Novo" }] })).EnsureSuccessStatusCode();
        }
        var resposta = await dev.PostAsJsonAsync("/api/remoto/comandos", PedidoDeDisparo(acao));
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("matrix-pendente", await CodigoAsync(resposta));
        Assert.Empty(_api.Fila.Enviadas);
        using var verificar = _api.Services.CreateScope();
        var banco = verificar.ServiceProvider.GetRequiredService<DevKitPageDb>();
        Assert.Empty(banco.ComandosRemotos);
        Assert.Empty(banco.EventosDaConversa);
    }

    [Theory]
    [InlineData("disparar", null)]
    [InlineData("disparar", 1)]
    [InlineData("retomar", null)]
    [InlineData("aprovar", null)]
    public async Task Disparo_remoto_definido_sem_resultados_enfileira_uma_vez(string acao, int? item)
    {
        var (_, dev) = await DevComFilaAsync();
        (await dev.PutAsJsonAsync("/api/projetos", ProjetoParaDisparo(SituacaoDaAcao(acao)))).EnsureSuccessStatusCode();
        await DefinirDisparoAsync(dev);
        var pedido = PedidoDeDisparo(acao, item);
        Assert.Equal(HttpStatusCode.Accepted, (await dev.PostAsJsonAsync("/api/remoto/comandos", pedido)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dev.PostAsJsonAsync("/api/remoto/comandos", pedido)).StatusCode);
        // O pedido vai à máquina tipado, com a ação pedida: o host lê a mesma ação e repete os portões.
        var mensagem = JsonSerializer.Deserialize<MensagemDaFilaV1>(Assert.Single(_api.Fila.Enviadas).Mensagem, Json)!;
        Assert.Equal(ConversaV1.ComandoProjeto, mensagem.Tipo);
        using (var texto = JsonDocument.Parse(mensagem.Texto))
        {
            Assert.Equal(acao, texto.RootElement.GetProperty("acao").GetString());
            Assert.Equal("disparo", texto.RootElement.GetProperty("projeto").GetString());
        }
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
        Assert.Single(db.ComandosRemotos);
        Assert.DoesNotContain(db.Set<DevKitPage.Core.RevisaoMatrixNoBanco>(), r => r.Tipo == "registro");
    }

    [Fact]
    public async Task Disparo_remoto_reconfere_matrix_ao_reenviar_falha_da_fila()
    {
        var (_, dev) = await DevComFilaAsync();
        (await dev.PutAsJsonAsync("/api/projetos", ProjetoParaDisparo())).EnsureSuccessStatusCode();
        await DefinirDisparoAsync(dev);
        var pedido = PedidoDeDisparo();
        _api.Fila.Falhar = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await dev.PostAsJsonAsync("/api/remoto/comandos", pedido)).StatusCode);
        var vazia = MatrizDoDisparo() with { Matriz = MatrizDoDisparo().Matriz! with { Celulas = [] } };
        (await dev.PutAsJsonAsync("/api/matrix/disparo/matriz/disparo?aplicar=true", new EdicaoMatrix(1, vazia))).EnsureSuccessStatusCode();
        _api.Fila.Falhar = false;
        Assert.Equal(HttpStatusCode.Conflict, (await dev.PostAsJsonAsync("/api/remoto/comandos", pedido)).StatusCode);
        Assert.Empty(_api.Fila.Enviadas);
        using var escopo = _api.Services.CreateScope();
        Assert.Equal(ConversaV1.EstadoFalhou, Assert.Single(escopo.ServiceProvider.GetRequiredService<DevKitPageDb>().ComandosRemotos).Estado);
    }
}
