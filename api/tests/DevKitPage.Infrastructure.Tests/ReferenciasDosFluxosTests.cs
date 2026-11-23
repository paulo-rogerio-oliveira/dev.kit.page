using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

public sealed class ReferenciasDosFluxosTests
{
    [Fact]
    public async Task Referencia_portatil_faz_roundtrip_e_cliente_antigo_nao_sobrescreve_por_id_local()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();
        var usuario = await baseDeTeste.ComAsync(sp => Task.FromResult(sp.GetRequiredService<DevKitPageDb>().Usuarios.Single().Id));
        var escopo = new EscopoDoDesign(usuario, null, false);
        var referencia = "ref:fluxo:identidade-global@" + new string('a', 64);
        var envio = new EnvioDeProjetoV1("referencias", "Referências", null, null, ["repo-a"], "feature/projeto", false,
            "", [], false, 1, "develop", "rascunho", null, [new(1, "Item", "", referencia, [], [], [], 1, 0, "planejado")])
            { FluxoPadrao = referencia };
        var criado = (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>().GravarAsync(escopo, envio, default))).Valor!.Projeto;
        Assert.Equal(referencia, criado.FluxoPadrao);
        Assert.Equal(referencia, criado.Itens[0].Fluxo);
        var antigo = envio with { Versao = criado.Versao, FluxoPadrao = null, Itens = [envio.Itens[0] with { Fluxo = "1" }] };
        Assert.Equal(FalhaNaGestao.Conflito, (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>()
            .GravarAsync(escopo, antigo, default))).Falha);
        var preservado = (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>()
            .GravarAsync(escopo, antigo with { Itens = [envio.Itens[0] with { Fluxo = null! }] }, default))).Valor!.Projeto;
        Assert.Equal(referencia, preservado.FluxoPadrao);
        Assert.Equal(referencia, preservado.Itens[0].Fluxo);
        var relido = (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>().ObterAsync(escopo, criado.Id, default)))!;
        Assert.Equal(referencia, relido.Itens[0].Fluxo);
    }
}
