using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// Os pedidos de demonstração na base SQLite de verdade (a migration aditiva aplicada): gravação,
/// paginação do mais novo para o mais antigo, exclusão e o expurgo além da retenção (LGPD). E o
/// script do Azure SQL conferido contra o modelo.
/// </summary>
public sealed class DemonstracoesTests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new(new() { ["Demonstracoes:RetencaoDias"] = "365" });

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<PedidoDeDemonstracaoCriadoV1> Registrar(string nome, string email = "ana@empresa.com.br")
        => _base.ComAsync(sp => sp.GetRequiredService<IPedidosDeDemonstracao>()
            .RegistrarAsync(new PedidoDeDemonstracaoV1($"  {nome}  ", email, null, "Quero ver o fluxo.", true), default));

    [Fact]
    public async Task Grava_o_pedido_com_os_textos_aparados_e_o_instante_do_consentimento()
    {
        var criado = await Registrar("Ana Souza");

        var gravado = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().PedidosDeDemonstracao.AsNoTracking().SingleAsync());
        Assert.Equal(criado.Id, gravado.Id);
        Assert.Equal("Ana Souza", gravado.Nome);
        Assert.Equal(string.Empty, gravado.Empresa);
        Assert.Equal(_base.Relogio.Agora.UtcDateTime, gravado.ConsentimentoEmUtc);
        Assert.Equal(_base.Relogio.Agora, criado.RecebidoEm);
    }

    [Fact]
    public async Task Lista_pagina_do_mais_novo_para_o_mais_antigo()
    {
        for (var i = 1; i <= 5; i++)
        {
            await Registrar($"Pessoa {i}");
            _base.Relogio.Agora = _base.Relogio.Agora.AddMinutes(1);
        }

        var pagina = await _base.ComAsync(sp => sp.GetRequiredService<IPedidosDeDemonstracao>().ListarAsync(2, 2, default));

        Assert.Equal(5, pagina.Total);
        Assert.Equal(["Pessoa 3", "Pessoa 2"], pagina.Itens.Select(p => p.Nome));
        Assert.Equal((2, 2), (pagina.NumeroDaPagina, pagina.Tamanho));
    }

    [Fact]
    public async Task Exclui_so_o_pedido_pedido_e_o_inexistente_e_falso()
    {
        var fica = await Registrar("Fica");
        var sai = await Registrar("Sai");
        var pedidos = (IServiceProvider sp) => sp.GetRequiredService<IPedidosDeDemonstracao>();

        Assert.True(await _base.ComAsync(sp => pedidos(sp).ExcluirAsync(sai.Id, default)));
        Assert.False(await _base.ComAsync(sp => pedidos(sp).ExcluirAsync(sai.Id, default)));

        var restantes = await _base.ComAsync(sp => pedidos(sp).ListarAsync(1, 20, default));
        Assert.Equal(fica.Id, Assert.Single(restantes.Itens).Id);
    }

    [Fact]
    public async Task Expurgo_remove_so_o_que_passou_da_retencao_e_mantem_os_recentes()
    {
        var inicio = _base.Relogio.Agora;
        _base.Relogio.Agora = inicio.AddDays(-366);
        await Registrar("Vencido");
        _base.Relogio.Agora = inicio.AddDays(-364);
        await Registrar("Recente");
        _base.Relogio.Agora = inicio;

        var apagados = await _base.ComAsync(sp => sp.GetRequiredService<IExpurgoDePedidos>().ExpurgarAsync(default));

        Assert.Equal(1, apagados);
        var restantes = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().PedidosDeDemonstracao.Select(p => p.Nome).ToListAsync());
        Assert.Equal(["Recente"], restantes);
    }

    [Fact]
    public async Task Expurgo_dos_pedidos_nao_toca_na_telemetria()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        var antigo = _base.Relogio.Agora.AddDays(-20);
        await _base.ComAsync(sp => sp.GetRequiredService<IIngestaoDeTelemetria>().RegistrarAsync(
            maquina, new TelemetryBatchV1("v1", "m-1", "1.4.0", [BaseDeTeste.Evento("e1", TiposDeEvento.SessaoIniciada, antigo)]), default));

        await _base.ComAsync(sp => sp.GetRequiredService<IExpurgoDePedidos>().ExpurgarAsync(default));

        Assert.Equal(1, await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Eventos.CountAsync()));
    }

    [Fact]
    public void Script_do_azure_sql_cria_a_tabela_com_as_mesmas_colunas_do_modelo()
    {
        var colunas = BaseDeTeste.ColunasNoSqlServer("PedidosDeDemonstracao");
        var script = BaseDeTeste.ScriptDoAzureSql("PedidosDeDemonstracao.sql");

        Assert.Equal(7, colunas.Count);
        foreach (var coluna in colunas)
            Assert.Contains(coluna, script);
        Assert.Contains("IF OBJECT_ID(N'[dbo].[PedidosDeDemonstracao]', N'U') IS NULL", script);
        Assert.Contains("IX_PedidosDeDemonstracao_RecebidoEmUtc", script);
    }
}
