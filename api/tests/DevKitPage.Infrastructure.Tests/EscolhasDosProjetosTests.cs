using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

public sealed class EscolhasDosProjetosTests
{
    private static EnvioDeProjetoV1 Envio() => new("escolhas", "Escolhas", null, null, ["https://github.com/acme/portal"],
        "feature/dev/projeto-escolhas", false, "", [], true, 2, "develop", ProjetosV1.Rascunho, null,
        [new(1, "Implementar", "", "42", [], [], [], 1, 0, ProjetosV1.ItemPlanejado)])
    {
        BaseDoBranch = "develop", FluxoPadrao = "42", Agente = "codex", Modelo = "modelo-concreto",
        Modelos = [new("codex", "modelo-concreto"), new("claude", "sonnet")],
        PoliticaDeFinalizacao = new("escolhas", "finalizacao-unica", 1, "42", DateTimeOffset.UtcNow, "escolha explícita")
        { IdentidadeDoFluxo = "fluxo:global", RevisaoDoFluxo = new string('a', 64), ModeloDaFinalizacao = new("claude", "sonnet") },
    };

    [Fact]
    public async Task Cliente_antigo_preserva_escolhas_e_design_e_remocao_explicita_e_respeitada()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();
        var usuario = await baseDeTeste.ComAsync(sp => Task.FromResult(sp.GetRequiredService<DevKitPageDb>().Usuarios.Single().Id));
        var escopo = new EscopoDoDesign(usuario, null, false);
        var envio = Envio();
        var proposta = new DesignDoItemV1(true, 2, [new("app", "tela", 3, new string('a', 64))]);
        envio = envio with { Itens = [envio.Itens[0] with { Design = proposta, Agente = "claude", Modelo = "sonnet" }] };
        var criado = (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>()
            .GravarAsync(escopo, envio, default))).Valor!.Projeto;

        var antigo = Envio() with
        {
            Versao = criado.Versao, BaseDoBranch = null, FluxoPadrao = null, Agente = null, Modelo = null, Modelos = null,
            PoliticaDeFinalizacao = null,
        };
        var preservado = (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>()
            .GravarAsync(escopo, antigo, default))).Valor!.Projeto;
        Assert.Equal(("develop", "42", "codex", "modelo-concreto"),
            (preservado.BaseDoBranch, preservado.FluxoPadrao, preservado.Agente, preservado.Modelo));
        Assert.Equal(envio.Modelos, preservado.Modelos);
        Assert.Equal(envio.PoliticaDeFinalizacao, preservado.PoliticaDeFinalizacao);
        Assert.Equal(("claude", "sonnet"), (preservado.Itens[0].Agente, preservado.Itens[0].Modelo));
        Assert.Equal(proposta.Revisao, preservado.Itens[0].Design!.Revisao);
        Assert.Equal(proposta.Propostas, preservado.Itens[0].Design!.Propostas);

        var conflito = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>()
            .GravarAsync(escopo, antigo with { FluxoPadrao = "outro" }, default));
        Assert.Equal(FalhaNaGestao.Conflito, conflito.Falha);

        var conflitoDePolitica = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>()
            .GravarAsync(escopo, antigo with { Versao = preservado.Versao,
                PoliticaDeFinalizacao = envio.PoliticaDeFinalizacao! with { Motivo = "Outra escolha na mesma revisão" } }, default));
        Assert.Equal(FalhaNaGestao.Conflito, conflitoDePolitica.Falha);

        var remover = antigo with { Versao = preservado.Versao, FluxoPadrao = "", Agente = "", Modelo = "", Modelos = [] };
        var removido = (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>()
            .GravarAsync(escopo, remover, default))).Valor!.Projeto;
        Assert.Equal("", removido.FluxoPadrao);
        Assert.Equal("", removido.Agente);
        Assert.Equal("", removido.Modelo);
        Assert.Empty(removido.Modelos!);
        Assert.Equal("sonnet", removido.Itens[0].Modelo);
    }
}
