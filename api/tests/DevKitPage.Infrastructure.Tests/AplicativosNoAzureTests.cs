using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O App Service e o Static Web App na base real (item 12): a cota por tipo ATÔMICA com pedidos simultâneos — e a da fila,
/// a ressalva do item 4 —, o índice único da vaga, o pedido repetido, o alerta e o teto do custo, a remoção com o segundo
/// fator e a falha parcial que guarda o passo feito e se repete dele.
/// </summary>
public sealed class AplicativosNoAzureTests
{
    private static readonly Dictionary<string, string?> Configuracao = new()
    {
        ["Provisao:MaximoDeTentativas"] = "3",
        ["Provisao:MaximoDeAppServicesPorUsuario"] = "2",
        ["Provisao:MaximoDeStaticWebAppsPorUsuario"] = "3",
        ["Provisao:LimiteDeCustoMensal"] = "150",
        ["Provisao:AlertaDeCustoPercentual"] = "80",
    };

    private static readonly PlanoDeAplicativoV1 B1 = new("B1", "B1 · Linux", 61.70m);
    private static readonly PlanoDeAplicativoV1 Free = new("Free", "Free", 0m);

    private static async Task<(BaseDeTeste Base, int Ana, int Bia)> BaseAsync()
    {
        var b = new BaseDeTeste(Configuracao);
        await b.InicializarAsync();
        var ids = await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var ana = new Usuario { Login = "ana", Papel = Papeis.Dev, CriadoEmUtc = b.Relogio.Agora.UtcDateTime };
            var bia = new Usuario { Login = "bia", Papel = Papeis.Dev, CriadoEmUtc = b.Relogio.Agora.UtcDateTime };
            db.Usuarios.AddRange(ana, bia);
            await db.SaveChangesAsync();
            return (ana.Id, bia.Id);
        });
        return (b, ids.Item1, ids.Item2);
    }

    private static Task<ResultadoDaGestao<PedidoAceito>> PedirAsync(BaseDeTeste b, int usuario, string rotulo,
        string tipo = ProvisaoV1.TipoAppService, PlanoDeAplicativoV1? plano = null, string regiao = "brazilsouth")
        => b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PedirAplicativoAsync(usuario, "ana", new AplicativoPedido(tipo, rotulo, regiao, plano ?? B1), default));

    private static Task<DetalheDoRecursoV1?> DetalheAsync(BaseDeTeste b, int usuario, long id)
        => b.ComAsync(sp => sp.GetRequiredService<IProvisao>().DetalheAsync(usuario, id, default));

    private static Task<CotaDaProvisaoV1> CotaAsync(BaseDeTeste b, int usuario)
        => b.ComAsync(sp => sp.GetRequiredService<IProvisao>().CotaAsync(usuario, default));

    /// <summary>Leva o pedido a pronto pelo motor, como o worker.</summary>
    private static async Task ProntoAsync(BaseDeTeste b, long id)
    {
        var reservado = await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ReservarAsync("w1", default));
        Assert.Equal(id, reservado!.Id);
        Assert.True(await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ConcluirAsync(id, "w1", new RecursoNaNuvem("/id", "https://x.azurewebsites.net"), default)));
    }

    [Fact]
    public async Task A_migracao_traz_as_colunas_e_o_script_do_azure_sql_cria_a_vaga_com_o_indice_unico_filtrado()
    {
        var (b, _, _) = await BaseAsync();
        await using var _ = b;

        var aplicadas = await b.ComAsync(async sp => (await sp.GetRequiredService<DevKitPageDb>().Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Contains(aplicadas, m => m.EndsWith("_AplicativosNoAzure", StringComparison.Ordinal));

        var script = BaseDeTeste.ScriptDoAzureSql("Provisao.sql");
        Assert.Contains("CREATE UNIQUE INDEX [IX_RecursosProvisionados_UsuarioId_Tipo_Vaga] ON [dbo].[RecursosProvisionados] ([UsuarioId], [Tipo], [Vaga]) WHERE [Estado] <> N'removido'", script);
        Assert.Contains("ALTER COLUMN [Nome] nvarchar(60) NOT NULL", script);
        foreach (var coluna in new[] { "Vaga", "Rotulo", "Plano", "NomeDoPlano", "CustoMensal", "Passos" })
            Assert.Equal(["Provisao.sql"], EsquemaDoBanco.ScriptsQueTrazem("RecursosProvisionados", coluna));
    }

    [Fact]
    public async Task Pedidos_simultaneos_nao_passam_da_cota_do_tipo()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;

        var resultados = await Task.WhenAll(Enumerable.Range(1, 6).Select(i => PedirAsync(b, ana, $"api-{i}", plano: Free)));

        Assert.Equal(2, resultados.Count(r => r.Valor is { Novo: true }));
        Assert.All(resultados.Where(r => r.Valor is null), r => Assert.Equal((FalhaNaGestao.Conflito, ProvisaoV1.CodigoCotaCheia), (r.Falha, r.Codigo)));
        var vagas = await b.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RecursosProvisionados
            .Where(r => r.UsuarioId == ana && r.Tipo == ProvisaoV1.TipoAppService).Select(r => r.Vaga).ToListAsync());
        Assert.Equal([1, 2], vagas.Order());

        // A cota é por TIPO: os Static Web Apps ainda cabem.
        Assert.True((await PedirAsync(b, ana, "site-docs", ProvisaoV1.TipoStaticWebApp, Free, "eastus2")).Valor!.Novo);
    }

    [Fact]
    public async Task Pedidos_simultaneos_da_fila_criam_uma_so_e_devolvem_a_mesma()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;

        var resultados = await Task.WhenAll(Enumerable.Range(1, 6).Select(_ =>
            b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PedirFilaAsync(ana, "ana", "brazilsouth", default))));

        Assert.Single(resultados, r => r.Valor!.Novo);
        Assert.Single(resultados.Select(r => r.Valor!.Recurso.Id).Distinct());
        Assert.Equal(1, await b.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RecursosProvisionados.CountAsync(r => r.UsuarioId == ana)));
    }

    [Fact]
    public async Task O_indice_unico_barra_a_mesma_vaga_ativa_mesmo_sem_a_trava_e_o_removido_a_devolve()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;
        RecursoProvisionado Recurso(string nome, string estado) => new()
        {
            UsuarioId = ana, Tipo = ProvisaoV1.TipoAppService, Vaga = 1, Nome = nome, Regiao = "brazilsouth", GrupoDeRecursos = "rg",
            Operacao = ProvisaoV1.OperacaoCriar, Estado = estado, CriadoEmUtc = b.Relogio.Agora.UtcDateTime, AtualizadoEmUtc = b.Relogio.Agora.UtcDateTime,
        };

        await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            db.RecursosProvisionados.AddRange(Recurso("app-removido", ProvisaoV1.EstadoRemovido), Recurso("app-ativo", ProvisaoV1.EstadoPronto));
            return await db.SaveChangesAsync();
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            db.RecursosProvisionados.Add(Recurso("app-concorrente", ProvisaoV1.EstadoPedido));
            return await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task O_pedido_grava_os_nomes_no_azure_e_o_mesmo_nome_volta_como_esta()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;

        var primeiro = (await PedirAsync(b, ana, "api-pedidos")).Valor!;
        var repetido = (await PedirAsync(b, ana, "api-pedidos")).Valor!;

        Assert.True(primeiro.Novo);
        Assert.False(repetido.Novo);
        Assert.Equal(primeiro.Recurso.Id, repetido.Recurso.Id);
        var r = primeiro.Recurso;
        Assert.Matches($"^app-u[0-9a-z]+-api-pedidos-[0-9a-z]{{4}}$", r.Nome);
        Assert.Equal("asp" + r.Nome[3..], r.NomeDoPlano);
        Assert.Equal(("api-pedidos", "B1", 61.70m, $"rg-devkit-u{ana}", ProvisaoV1.EstadoPedido), (r.Rotulo, r.Plano, r.CustoMensal, r.GrupoDeRecursos, r.Estado));
        Assert.Empty(r.Passos!);
        Assert.False(r.Parcial);

        var pedido = Assert.Single((await DetalheAsync(b, ana, r.Id))!.Eventos);
        Assert.Contains("cota reservada (1 de 2)", pedido.Detalhe, StringComparison.Ordinal);

        var site = (await PedirAsync(b, ana, "site-docs", ProvisaoV1.TipoStaticWebApp, Free, "eastus2")).Valor!.Recurso;
        Assert.StartsWith("swa-u", site.Nome, StringComparison.Ordinal);
        Assert.Equal(string.Empty, site.NomeDoPlano);
    }

    [Fact]
    public async Task O_custo_avisa_no_alerta_e_o_pedido_que_passaria_do_teto_e_recusado()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;

        var primeiro = (await PedirAsync(b, ana, "api-pedidos")).Valor!.Recurso;
        Assert.False((await CotaAsync(b, ana)).Alerta);
        Assert.DoesNotContain((await DetalheAsync(b, ana, primeiro.Id))!.Eventos, e => e.Evento == ProvisaoV1.EventoAlertaDeCusto);

        // 61,70 + 61,70 = 123,40: passou de 80% de 150 (120) — o alerta vai para a auditoria do pedido que o cruzou.
        var segundo = (await PedirAsync(b, ana, "api-relatorios")).Valor!.Recurso;
        var alerta = Assert.Single((await DetalheAsync(b, ana, segundo.Id))!.Eventos, e => e.Evento == ProvisaoV1.EventoAlertaDeCusto);
        Assert.Equal("80% do limite do mês: R$ 123,40 de R$ 150,00", alerta.Detalhe);

        var cota = await CotaAsync(b, ana);
        Assert.Equal((123.40m, 150m, 80, true, false), (cota.CustoDoMes, cota.LimiteDeCusto, cota.AlertaDeCustoPercentual, cota.Alerta, cota.Bloqueado));
        Assert.Equal([new UsoDoTipoV1(ProvisaoV1.TipoAppService, 2, 2), new UsoDoTipoV1(ProvisaoV1.TipoStaticWebApp, 0, 3)], cota.Tipos);

        // O Static Web App pago (48,00) passaria de 150: recusado com o código; o gratuito cabe.
        var caro = await PedirAsync(b, ana, "site-loja", ProvisaoV1.TipoStaticWebApp, new PlanoDeAplicativoV1("Standard", "Standard", 48m), "eastus2");
        Assert.Equal((FalhaNaGestao.Conflito, ProvisaoV1.CodigoCustoNoLimite), (caro.Falha, caro.Codigo));
        Assert.Contains("passaria do seu limite de R$ 150,00", caro.Mensagem, StringComparison.Ordinal);
        Assert.True((await PedirAsync(b, ana, "site-docs", ProvisaoV1.TipoStaticWebApp, Free, "eastus2")).Valor!.Novo);
    }

    [Fact]
    public async Task A_remocao_do_aplicativo_pede_o_segundo_fator_e_a_da_fila_nao()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;
        var fila = (await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PedirFilaAsync(ana, "ana", "brazilsouth", default))).Valor!.Recurso.Id;
        await ProntoAsync(b, fila);
        var id = (await PedirAsync(b, ana, "api-pedidos")).Valor!.Recurso.Id;
        await ProntoAsync(b, id);
        Task<ResultadoDaGestao<PedidoAceito>> RemoverAsync(string? codigo)
            => b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RemoverAsync(ana, "ana", id, codigo, default));

        var semCodigo = await RemoverAsync(null);
        Assert.Equal((FalhaNaGestao.Proibida, ProvisaoV1.CodigoSegundoFator), (semCodigo.Falha, semCodigo.Codigo));
        var semAutenticador = await RemoverAsync("123456");
        Assert.Equal(FalhaNaGestao.Proibida, semAutenticador.Falha);
        Assert.StartsWith("Configure o aplicativo autenticador antes de remover", semAutenticador.Mensagem, StringComparison.Ordinal);

        var chave = await SegundoFatorDeTeste.AtivarAsync(b, ana);
        var errado = SegundoFatorDeTeste.Codigo(b, chave) == "000000" ? "111111" : "000000";
        Assert.Equal(ProvisaoV1.CodigoSegundoFator, (await RemoverAsync(errado)).Codigo);
        Assert.Equal(ProvisaoV1.EstadoPronto, (await DetalheAsync(b, ana, id))!.Recurso.Estado);

        var pedida = (await RemoverAsync(SegundoFatorDeTeste.Codigo(b, chave))).Valor!;
        Assert.True(pedida.Novo);
        Assert.Equal((ProvisaoV1.OperacaoRemover, ProvisaoV1.EstadoPedido), (pedida.Recurso.Operacao, pedida.Recurso.Estado));
        var auditoria = (await DetalheAsync(b, ana, id))!.Eventos.Last();
        Assert.Equal(ProvisaoV1.EventoRemocaoPedida, auditoria.Evento);
        Assert.EndsWith($"e o plano {pedida.Recurso.NomeDoPlano} · segundo fator conferido", auditoria.Detalhe, StringComparison.Ordinal);
        // A remoção já pedida volta como está, sem pedir o código de novo.
        Assert.False((await RemoverAsync(null)).Valor!.Novo);

        Assert.True((await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RemoverAsync(ana, "ana", fila, null, default))).Valor!.Novo);
    }

    [Fact]
    public async Task A_falha_parcial_guarda_o_passo_feito_a_repeticao_o_mantem_e_a_remocao_libera_a_vaga()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;
        IMotorDeProvisao Motor(IServiceProvider sp) => sp.GetRequiredService<IMotorDeProvisao>();
        await ProntoAsync(b, (await PedirAsync(b, ana, "api-pedidos", plano: Free)).Valor!.Recurso.Id);
        var id = (await PedirAsync(b, ana, "api-relatorios")).Valor!.Recurso.Id;

        await b.ComAsync(sp => Motor(sp).ReservarAsync("w1", default));
        Assert.False(await b.ComAsync(sp => Motor(sp).RegistrarPassoAsync(id, "outro", ProvisaoV1.PassoPlano, default)));
        Assert.True(await b.ComAsync(sp => Motor(sp).RegistrarPassoAsync(id, "w1", ProvisaoV1.PassoPlano, default)));
        await b.ComAsync(sp => Motor(sp).FalharAsync(id, "w1", "O Azure recusou o App Service (409 Conflict): nome em uso.", transitoria: false, default));

        var parcial = (await DetalheAsync(b, ana, id))!;
        Assert.Equal((ProvisaoV1.EstadoFalhou, true), (parcial.Recurso.Estado, parcial.Recurso.Parcial));
        Assert.Equal([ProvisaoV1.PassoPlano], parcial.Recurso.Passos);
        var passo = Assert.Single(parcial.Eventos, e => e.Evento == ProvisaoV1.EventoPasso);
        Assert.Equal($"plano {parcial.Recurso.NomeDoPlano} criado", passo.Detalhe);

        var repetido = (await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RepetirAsync(ana, "ana", id, default))).Valor!.Recurso;
        Assert.Equal([ProvisaoV1.PassoPlano], repetido.Passos);
        Assert.False(repetido.Parcial);
        Assert.Contains((await DetalheAsync(b, ana, id))!.Eventos, e => e.Evento == ProvisaoV1.EventoRepeticao && e.Detalhe == "criação · retoma depois de: plano");

        // Falhou de novo e o usuário remove (com o segundo fator): a remoção concluída zera os passos e devolve a vaga.
        await b.ComAsync(sp => Motor(sp).ReservarAsync("w1", default));
        await b.ComAsync(sp => Motor(sp).FalharAsync(id, "w1", "409", transitoria: false, default));
        var chave = await SegundoFatorDeTeste.AtivarAsync(b, ana);
        Assert.Equal(FalhaNaGestao.Conflito, (await PedirAsync(b, ana, "api-nova", plano: Free)).Falha);
        await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RemoverAsync(ana, "ana", id, SegundoFatorDeTeste.Codigo(b, chave), default));
        await b.ComAsync(sp => Motor(sp).ReservarAsync("w1", default));
        Assert.True(await b.ComAsync(sp => Motor(sp).ConcluirAsync(id, "w1", null, default)));

        var removido = (await DetalheAsync(b, ana, id))!;
        Assert.Equal((ProvisaoV1.EstadoRemovido, 0), (removido.Recurso.Estado, removido.Recurso.Passos!.Count));
        Assert.Equal($"aplicativo {removido.Recurso.Nome} e plano {removido.Recurso.NomeDoPlano} removidos", removido.Eventos.Last().Detalhe);
        Assert.True((await PedirAsync(b, ana, "api-nova", plano: Free)).Valor!.Novo);
        Assert.Equal(0m, (await CotaAsync(b, ana)).CustoDoMes);
    }
}
