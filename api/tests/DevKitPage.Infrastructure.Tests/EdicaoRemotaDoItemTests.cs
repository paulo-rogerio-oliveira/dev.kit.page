using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

public sealed class EdicaoRemotaDoItemTests
{
    [Fact]
    public async Task Grava_e_rele_em_conexao_independente_conflito_e_cancelamento_preservam_banco()
    {
        await using var banco = new BaseDeTeste();
        await banco.InicializarAsync();
        var usuario = await banco.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Select(u => u.Id).SingleAsync());
        var escopo = new EscopoDoDesign(usuario, null, false);
        var envio = new EnvioDeProjetoV1("edicao", "Edição", "usuario", "", ["repo"], "feature/edicao", false,
            "Definição", [], true, 2, "develop", "rascunho", null,
            [new(1, "Original", "Descrição", "14", [], ["web"], [], 1, 0, "planejado", Agente: "codex", Modelo: "modelo")]);
        var p = await banco.ComAsync(async sp => (await sp.GetRequiredService<IProjetosDaEmpresa>()
            .GravarAsync(escopo, envio, default)).Valor!.Projeto);
        var edicao = new EdicaoRemotaDoItemV1(p.Versao, "Editado", "Nova descrição", "14", "codex", "modelo", [], ["web"], 1);
        var preparado = EdicaoRemotaDoItem.Preparar(p, 1, edicao, banco.Relogio.Agora);
        var gravado = await banco.ComAsync(sp => sp.GetRequiredService<IProjetosCompartilhados>().GravarAsync(escopo, p.Id, preparado, default));
        Assert.Equal(200, gravado.Status);
        // Novo escopo/DbContext abre outra conexão ao mesmo arquivo SQLite físico.
        var relido = await banco.ComAsync(sp => sp.GetRequiredService<IProjetosCompartilhados>().ObterAsync(escopo, p.Id, default));
        Assert.Equal("Editado", relido!.Itens[0].Titulo);
        Assert.Equal(p.Versao + 1, relido.Versao);
        Assert.Equal("aguardando", relido.Situacao);
        var antes = JsonSerializer.Serialize(relido);
        var atrasado = await banco.ComAsync(sp => sp.GetRequiredService<IProjetosCompartilhados>().GravarAsync(escopo, p.Id, preparado, default));
        Assert.Equal(409, atrasado.Status);
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => banco.ComAsync(sp => sp.GetRequiredService<IProjetosCompartilhados>()
            .GravarAsync(escopo, p.Id, preparado with { Versao = relido.Versao }, cancelamento.Token)));
        var depois = await banco.ComAsync(sp => sp.GetRequiredService<IProjetosCompartilhados>().ObterAsync(escopo, p.Id, default));
        Assert.Equal(antes, JsonSerializer.Serialize(depois));
    }
}
