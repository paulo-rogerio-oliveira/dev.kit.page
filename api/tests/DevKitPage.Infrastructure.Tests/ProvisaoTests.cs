using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A provisão na base real: o script do Azure SQL, a migration, o pedido idempotente na cota, o isolamento, o motor do worker
/// (a reserva disputada, a falha transitória que espera e repete, a definitiva, as tentativas esgotadas, a reserva vencida),
/// a repetição e a remoção pelo usuário, e a liberação pelo admin — tudo com a linha na auditoria.
/// </summary>
public sealed class ProvisaoTests
{
    private static readonly Dictionary<string, string?> Configuracao = new()
    {
        ["Provisao:MaximoDeTentativas"] = "3",
        ["Provisao:EsperaInicialSegundos"] = "30",
        ["Provisao:EsperaMaximaSegundos"] = "600",
        ["Provisao:ReservaMinutos"] = "10",
    };

    private static async Task<(BaseDeTeste Base, int Ana, int Bia)> BaseAsync()
    {
        var baseDeTeste = new BaseDeTeste(Configuracao);
        await baseDeTeste.InicializarAsync();
        var ids = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var ana = new Usuario { Login = "ana", Papel = Papeis.Dev, CriadoEmUtc = baseDeTeste.Relogio.Agora.UtcDateTime };
            var bia = new Usuario { Login = "bia", Papel = Papeis.Dev, CriadoEmUtc = baseDeTeste.Relogio.Agora.UtcDateTime };
            db.Usuarios.AddRange(ana, bia);
            await db.SaveChangesAsync();
            return (ana.Id, bia.Id);
        });
        return (baseDeTeste, ids.Item1, ids.Item2);
    }

    private static Task<ResultadoDaGestao<PedidoAceito>> PedirAsync(BaseDeTeste b, int usuario, string regiao = "brazilsouth")
        => b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PedirFilaAsync(usuario, "ana", regiao, default));

    private static Task<RecursoProvisionado?> ReservarAsync(BaseDeTeste b, string trabalhador = "w1")
        => b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ReservarAsync(trabalhador, default));

    private static Task<DetalheDoRecursoV1?> DetalheAsync(BaseDeTeste b, int usuario, long id)
        => b.ComAsync(sp => sp.GetRequiredService<IProvisao>().DetalheAsync(usuario, id, default));

    [Fact]
    public void Script_do_azure_sql_cria_as_tabelas_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Provisao.sql");

        foreach (var tabela in new[] { "PermissoesDeUsuario", "RecursosProvisionados", "EventosDaProvisao" })
        {
            Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
            foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
                Assert.Contains(coluna, script);
            Assert.Equal(["Provisao.sql"], EsquemaDoBanco.ScriptsQueTrazem(tabela, null));
        }
        Assert.Contains("CREATE UNIQUE INDEX [IX_RecursosProvisionados_Nome]", script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_PermissoesDeUsuario_UsuarioId_Recurso]", script);
    }

    [Fact]
    public async Task A_migracao_cria_as_tabelas_e_nao_deixa_mudanca_de_modelo_pendente()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();

        var (aplicadas, pendente) = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            return ((await db.Database.GetAppliedMigrationsAsync()).ToArray(), db.Database.HasPendingModelChanges());
        });

        Assert.Contains(aplicadas, m => m.EndsWith("_ProvisaoNoAzure", StringComparison.Ordinal));
        Assert.False(pendente);
    }

    [Fact]
    public async Task O_pedido_grava_nomes_unicos_e_a_auditoria_e_o_repetido_devolve_a_mesma_fila()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;

        var primeiro = (await PedirAsync(b, ana)).Valor!;
        var segundo = (await PedirAsync(b, ana, "eastus2")).Valor!;

        Assert.True(primeiro.Novo);
        Assert.False(segundo.Novo);
        Assert.Equal(primeiro.Recurso.Id, segundo.Recurso.Id);
        Assert.Equal("brazilsouth", segundo.Recurso.Regiao);
        Assert.Equal(ProvisaoV1.EstadoPedido, primeiro.Recurso.Estado);
        Assert.Equal($"rg-devkit-u{ana}", primeiro.Recurso.GrupoDeRecursos);
        Assert.True(RegrasDaProvisao.ContaValida(primeiro.Recurso.Nome));
        Assert.Equal(ProvisaoV1.NomeDaFila, primeiro.Recurso.Fila);

        var auditoria = (await DetalheAsync(b, ana, primeiro.Recurso.Id))!.Eventos;
        var pedido = Assert.Single(auditoria);
        Assert.Equal((ProvisaoV1.EventoPedido, "ana"), (pedido.Evento, pedido.Autor));
        Assert.Contains(primeiro.Recurso.Nome, pedido.Detalhe);
        Assert.Equal(1, await b.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RecursosProvisionados.CountAsync()));
    }

    [Fact]
    public async Task O_recurso_de_outro_usuario_nao_existe_para_ele()
    {
        var (b, ana, bia) = await BaseAsync();
        await using var _ = b;
        var recurso = (await PedirAsync(b, ana)).Valor!.Recurso;

        Assert.Null(await DetalheAsync(b, bia, recurso.Id));
        Assert.Empty(await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RecursosAsync(bia, default)));
        var remocao = await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RemoverAsync(bia, "bia", recurso.Id, null, default));
        Assert.Equal(FalhaNaGestao.NaoEncontrado, remocao.Falha);
    }

    [Fact]
    public async Task O_worker_reserva_executa_e_conclui_e_dois_workers_nao_pegam_o_mesmo_pedido()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;
        var recurso = (await PedirAsync(b, ana)).Valor!.Recurso;

        var reservado = await ReservarAsync(b, "w1");
        Assert.Equal((recurso.Id, ProvisaoV1.EstadoEmProvisao, 1), (reservado!.Id, reservado.Estado, reservado.Tentativas));
        Assert.Null(await ReservarAsync(b, "w2"));

        // Só quem reservou conclui.
        Assert.False(await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ConcluirAsync(recurso.Id, "w2", null, default)));
        Assert.True(await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>()
            .ConcluirAsync(recurso.Id, "w1", new RecursoNaNuvem("/subscriptions/x/storageAccounts/y", "https://y.queue.core.windows.net/comandos"), default)));

        var detalhe = (await DetalheAsync(b, ana, recurso.Id))!;
        Assert.Equal(ProvisaoV1.EstadoPronto, detalhe.Recurso.Estado);
        Assert.Equal("https://y.queue.core.windows.net/comandos", detalhe.Recurso.Endpoint);
        Assert.NotNull(detalhe.Recurso.ProntoEm);
        Assert.Equal([ProvisaoV1.EventoPedido, ProvisaoV1.EventoInicio, ProvisaoV1.EventoPronto], detalhe.Eventos.Select(e => e.Evento));
        Assert.All(detalhe.Eventos.Skip(1), e => Assert.Equal(ProvisaoV1.AutorWorker, e.Autor));
    }

    [Fact]
    public async Task A_reserva_disputada_ao_mesmo_tempo_fica_com_um_worker_so()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;
        await PedirAsync(b, ana);

        var reservas = await Task.WhenAll(Enumerable.Range(1, 4).Select(i => ReservarAsync(b, $"w{i}")));

        Assert.Single(reservas, r => r is not null);
        var gravado = await b.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RecursosProvisionados.SingleAsync());
        Assert.Equal(1, gravado.Tentativas);
    }

    [Fact]
    public async Task A_falha_transitoria_espera_com_recuo_e_repete_ate_dar_certo()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;
        var id = (await PedirAsync(b, ana)).Valor!.Recurso.Id;

        await ReservarAsync(b);
        Assert.True(await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().FalharAsync(id, "w1", "O Azure respondeu 503.", transitoria: true, default)));

        var esperando = (await DetalheAsync(b, ana, id))!.Recurso;
        Assert.Equal(ProvisaoV1.EstadoPedido, esperando.Estado);
        Assert.Equal(b.Relogio.Agora.AddSeconds(30), esperando.ProximaTentativaEm);
        Assert.Null(await ReservarAsync(b));

        b.Relogio.Agora = b.Relogio.Agora.AddSeconds(31);
        var segunda = await ReservarAsync(b);
        Assert.Equal(2, segunda!.Tentativas);
        await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().FalharAsync(id, "w1", "429", transitoria: true, default));
        Assert.Equal(b.Relogio.Agora.AddSeconds(60), (await DetalheAsync(b, ana, id))!.Recurso.ProximaTentativaEm);

        b.Relogio.Agora = b.Relogio.Agora.AddSeconds(61);
        await ReservarAsync(b);
        await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ConcluirAsync(id, "w1", new RecursoNaNuvem("id", "https://x"), default));

        var detalhe = (await DetalheAsync(b, ana, id))!;
        Assert.Equal((ProvisaoV1.EstadoPronto, 3, string.Empty), (detalhe.Recurso.Estado, detalhe.Recurso.Tentativas, detalhe.Recurso.Erro));
        Assert.Equal(2, detalhe.Eventos.Count(e => e.Evento == ProvisaoV1.EventoFalhaTransitoria));
        Assert.Contains(detalhe.Eventos, e => e.Detalhe.Contains("nova tentativa em 30 s", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_falha_definitiva_para_na_hora_e_a_transitoria_para_ao_esgotar_as_tentativas()
    {
        var (b, ana, bia) = await BaseAsync();
        await using var _ = b;
        var definitiva = (await PedirAsync(b, ana)).Valor!.Recurso.Id;
        await ReservarAsync(b);
        await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().FalharAsync(definitiva, "w1", "A identidade não tem permissão.", transitoria: false, default));
        var falhou = (await DetalheAsync(b, ana, definitiva))!.Recurso;
        Assert.Equal((ProvisaoV1.EstadoFalhou, "A identidade não tem permissão."), (falhou.Estado, falhou.Erro));

        var esgotada = (await PedirAsync(b, bia)).Valor!.Recurso.Id;
        for (var i = 0; i < 3; i++)
        {
            b.Relogio.Agora = b.Relogio.Agora.AddHours(1);
            Assert.NotNull(await ReservarAsync(b));
            await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().FalharAsync(esgotada, "w1", "503", transitoria: true, default));
        }
        var detalhe = (await DetalheAsync(b, bia, esgotada))!;
        Assert.Equal((ProvisaoV1.EstadoFalhou, 3), (detalhe.Recurso.Estado, detalhe.Recurso.Tentativas));
        Assert.Contains("3 tentativas esgotadas", detalhe.Eventos.Last().Detalhe, StringComparison.Ordinal);
        b.Relogio.Agora = b.Relogio.Agora.AddHours(1);
        Assert.Null(await ReservarAsync(b));
    }

    [Fact]
    public async Task O_que_falhou_se_repete_do_comeco_e_o_que_esta_em_curso_nao()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;
        var id = (await PedirAsync(b, ana)).Valor!.Recurso.Id;

        var cedo = await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RepetirAsync(ana, "ana", id, default));
        Assert.Equal(FalhaNaGestao.Conflito, cedo.Falha);

        await ReservarAsync(b);
        await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().FalharAsync(id, "w1", "403", transitoria: false, default));
        var repetido = await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RepetirAsync(ana, "ana", id, default));

        Assert.Equal((ProvisaoV1.EstadoPedido, 0, string.Empty), (repetido.Valor!.Recurso.Estado, repetido.Valor.Recurso.Tentativas, repetido.Valor.Recurso.Erro));
        Assert.Equal(1, (await ReservarAsync(b))!.Tentativas);
        Assert.Contains((await DetalheAsync(b, ana, id))!.Eventos, e => e.Evento == ProvisaoV1.EventoRepeticao && e.Autor == "ana");
    }

    [Fact]
    public async Task A_remocao_espera_a_criacao_e_depois_vai_ao_worker_e_libera_a_cota()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;
        var id = (await PedirAsync(b, ana)).Valor!.Recurso.Id;
        IProvisao Provisao(IServiceProvider sp) => sp.GetRequiredService<IProvisao>();

        Assert.Equal(FalhaNaGestao.Conflito, (await b.ComAsync(sp => Provisao(sp).RemoverAsync(ana, "ana", id, null, default))).Falha);

        await ReservarAsync(b);
        await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ConcluirAsync(id, "w1", new RecursoNaNuvem("id", "https://x"), default));
        var pedida = (await b.ComAsync(sp => Provisao(sp).RemoverAsync(ana, "ana", id, null, default))).Valor!;
        Assert.True(pedida.Novo);
        Assert.Equal((ProvisaoV1.OperacaoRemover, ProvisaoV1.EstadoPedido), (pedida.Recurso.Operacao, pedida.Recurso.Estado));
        Assert.False((await b.ComAsync(sp => Provisao(sp).RemoverAsync(ana, "ana", id, null, default))).Valor!.Novo);

        var reservada = await ReservarAsync(b);
        Assert.Equal(ProvisaoV1.OperacaoRemover, reservada!.Operacao);
        await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ConcluirAsync(id, "w1", null, default));

        var removido = (await DetalheAsync(b, ana, id))!;
        Assert.Equal((ProvisaoV1.EstadoRemovido, string.Empty), (removido.Recurso.Estado, removido.Recurso.Endpoint));
        Assert.Equal(
            [ProvisaoV1.EventoPedido, ProvisaoV1.EventoInicio, ProvisaoV1.EventoPronto, ProvisaoV1.EventoRemocaoPedida, ProvisaoV1.EventoInicio, ProvisaoV1.EventoRemovido],
            removido.Eventos.Select(e => e.Evento));

        // A removida sai da cota: o pedido novo cria OUTRA fila, com outro nome.
        var nova = (await PedirAsync(b, ana)).Valor!;
        Assert.True(nova.Novo);
        Assert.NotEqual(removido.Recurso.Nome, nova.Recurso.Nome);
    }

    [Fact]
    public async Task A_reserva_vencida_volta_a_outro_worker_e_a_que_cai_sempre_desiste()
    {
        var (b, ana, _) = await BaseAsync();
        await using var _ = b;
        var id = (await PedirAsync(b, ana)).Valor!.Recurso.Id;

        await ReservarAsync(b, "caiu");
        b.Relogio.Agora = b.Relogio.Agora.AddMinutes(11);
        var retomada = await ReservarAsync(b, "w2");
        Assert.Equal(("w2", 2), (retomada!.ReservadoPor, retomada.Tentativas));
        Assert.Contains((await DetalheAsync(b, ana, id))!.Eventos, e => e.Detalhe.Contains("retomada", StringComparison.Ordinal));
        // O que caiu não grava mais nada.
        Assert.False(await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ConcluirAsync(id, "caiu", null, default)));

        b.Relogio.Agora = b.Relogio.Agora.AddMinutes(11);
        await ReservarAsync(b, "w3");
        b.Relogio.Agora = b.Relogio.Agora.AddMinutes(11);
        Assert.Null(await ReservarAsync(b, "w4"));

        var desistiu = (await DetalheAsync(b, ana, id))!.Recurso;
        Assert.Equal(ProvisaoV1.EstadoFalhou, desistiu.Estado);
        Assert.Contains("não terminou em 3 tentativas", desistiu.Erro, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_admin_libera_e_retira_com_auditoria_e_a_permissao_decide_a_origem()
    {
        var (b, ana, bia) = await BaseAsync();
        await using var _ = b;
        IGestaoDeUsuarios Gestao(IServiceProvider sp) => sp.GetRequiredService<IGestaoDeUsuarios>();

        Assert.False((await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PermissaoAsync(ana, default))).Liberada);

        var liberado = await b.ComAsync(sp => Gestao(sp).LiberarRecursoAsync(ana, ProvisaoV1.RecursoProvisao, true, "admin", default));
        await b.ComAsync(sp => Gestao(sp).LiberarRecursoAsync(ana, ProvisaoV1.RecursoProvisao, true, "admin", default));
        Assert.Equal([ProvisaoV1.RecursoProvisao], liberado.Valor!.RecursosLiberados);
        var permissao = await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PermissaoAsync(ana, default));
        Assert.Equal((true, ProvisaoV1.OrigemPermissao, 1), (permissao.Liberada, permissao.Origem, permissao.MaximoDeFilas));
        Assert.False((await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PermissaoAsync(bia, default))).Liberada);

        var lista = await b.ComAsync(sp => Gestao(sp).ListarAsync(default));
        Assert.Equal([ProvisaoV1.RecursoProvisao], lista.Single(u => u.Id == ana).RecursosLiberados);
        Assert.Empty(lista.Single(u => u.Id == bia).RecursosLiberados!);

        var retirado = await b.ComAsync(sp => Gestao(sp).LiberarRecursoAsync(ana, ProvisaoV1.RecursoProvisao, false, "admin", default));
        Assert.Empty(retirado.Valor!.RecursosLiberados!);
        Assert.False((await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PermissaoAsync(ana, default))).Liberada);

        // Liberar duas vezes grava UMA linha de auditoria.
        var auditoria = await b.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().EventosDaProvisao.Where(e => e.UsuarioId == ana).Select(e => e.Evento).ToListAsync());
        Assert.Equal([ProvisaoV1.EventoLiberado, ProvisaoV1.EventoRetirado], auditoria);

        Assert.Equal(FalhaNaGestao.Recusada, (await b.ComAsync(sp => Gestao(sp).LiberarRecursoAsync(ana, "app-service", true, "admin", default))).Falha);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await b.ComAsync(sp => Gestao(sp).LiberarRecursoAsync(999, ProvisaoV1.RecursoProvisao, true, "admin", default))).Falha);
    }

    [Fact]
    public async Task O_plano_configurado_libera_sem_a_permissao_o_tier_de_assinatura()
    {
        await using var b = new BaseDeTeste(new Dictionary<string, string?>(Configuracao) { ["Provisao:PlanosQueLiberam:0"] = "Empresarial" });
        await b.InicializarAsync();
        var (comPlano, semPlano) = await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var empresarial = new Empresa { Nome = "Acme", Plano = "Empresarial", CodigoDeAdesao = "DK-AAAA-0001", CriadaEmUtc = b.Relogio.Agora.UtcDateTime };
            var basica = new Empresa { Nome = "Beta", Plano = "Básico", CodigoDeAdesao = "DK-BBBB-0002", CriadaEmUtc = b.Relogio.Agora.UtcDateTime };
            var ana = new Usuario { Login = "ana", Papel = Papeis.Dev, Empresa = empresarial, CriadoEmUtc = b.Relogio.Agora.UtcDateTime };
            var bia = new Usuario { Login = "bia", Papel = Papeis.Dev, Empresa = basica, CriadoEmUtc = b.Relogio.Agora.UtcDateTime };
            db.Usuarios.AddRange(ana, bia);
            await db.SaveChangesAsync();
            return (ana.Id, bia.Id);
        });

        var plano = await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PermissaoAsync(comPlano, default));
        Assert.Equal((true, ProvisaoV1.OrigemPlano), (plano.Liberada, plano.Origem));
        Assert.False((await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PermissaoAsync(semPlano, default))).Liberada);
    }
}
