using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A base embarcada de verdade: a semente do admin (uma vez só), a ingestão idempotente com o total
/// diário consolidado, o eventId único, as consultas agregadas e o expurgo que não mexe nos totais.
/// </summary>
public sealed class InfraestruturaTests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new();
    private static readonly DateTimeOffset Hoje = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<BatchResultV1> Enviar(int maquina, params TelemetryEventV1[] eventos) => EnviarNaVersao(maquina, "1.4.0", eventos);

    private Task<BatchResultV1> EnviarNaVersao(int maquina, string versao, params TelemetryEventV1[] eventos)
        => _base.ComAsync(sp => sp.GetRequiredService<IIngestaoDeTelemetria>()
            .RegistrarAsync(maquina, new TelemetryBatchV1("v1", "x", versao, eventos), default));

    private const string TraceDoDevKit = "System.InvalidOperationException: Sequence contains no elements\n   at GitKit.Core.Services.Planejador.Escolher() linha 42\n   at GitKit.App.Services.BackgroundJobService.RodarAsync() linha 1161";

    private static readonly Periodo Outubro = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

    [Fact]
    public async Task Base_nova_tem_exatamente_um_admin_que_deve_trocar_a_senha_e_a_segunda_subida_nao_altera()
    {
        await _base.InicializarAsync(); // a segunda subida

        var usuarios = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.AsNoTracking().ToListAsync());
        var admin = Assert.Single(usuarios);
        Assert.Equal("admin", admin.Login);
        Assert.True(admin.EhAdmin);
        Assert.True(admin.DeveTrocarSenha);
        Assert.DoesNotContain("senhaInicial2026", admin.SenhaHash);
        var hasher = _base.Servicos.GetRequiredService<IPasswordHasher<Usuario>>();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(admin, admin.SenhaHash, "senhaInicial2026"));
    }

    [Fact]
    public async Task Reenvio_do_mesmo_lote_nao_altera_os_totais()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        var eventos = new[]
        {
            BaseDeTeste.Evento("e1", TiposDeEvento.TurnoExecutado, Hoje, valor: 4000, detalhe: "claude"),
            BaseDeTeste.Evento("e2", TiposDeEvento.ArquivoAlterado, Hoje, quantidade: 3),
            BaseDeTeste.Evento("e2", TiposDeEvento.ArquivoAlterado, Hoje, quantidade: 3), // repetido no próprio lote
            BaseDeTeste.Evento("e3", "TipoDoFuturo", Hoje),
        };

        var primeiro = await Enviar(maquina, eventos);
        var segundo = await Enviar(maquina, eventos);

        Assert.Equal(new BatchResultV1(4, 2, 1, 1), primeiro);
        Assert.Equal(new BatchResultV1(4, 0, 3, 1), segundo);
        var quantidade = await _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().QuantidadeAsync(Outubro, null, default));
        Assert.Equal((1L, 3L), (quantidade.Turnos, quantidade.ArquivosAlterados));
    }

    [Fact]
    public async Task EventId_e_unico_no_banco()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        await Enviar(maquina, BaseDeTeste.Evento("e1", TiposDeEvento.SessaoIniciada, Hoje));

        await Assert.ThrowsAsync<DbUpdateException>(() => _base.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            db.Eventos.Add(new EventoDeUso { EventId = "e1", MaquinaId = maquina, Tipo = "SessaoIniciada", EmUtc = Hoje.UtcDateTime });
            return await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Quantidade_e_qualidade_somam_por_periodo_e_filtram_por_maquina()
    {
        var m1 = await _base.MaquinaAsync("m-1");
        var m2 = await _base.MaquinaAsync("m-2");
        await Enviar(m1,
            BaseDeTeste.Evento("a1", TiposDeEvento.SessaoIniciada, Hoje, detalhe: "1.4.0"),
            BaseDeTeste.Evento("a2", TiposDeEvento.TurnoExecutado, Hoje, valor: 3000, detalhe: "claude"),
            BaseDeTeste.Evento("a3", TiposDeEvento.TurnoExecutado, Hoje.AddDays(-1), valor: 1000, detalhe: "claude"),
            BaseDeTeste.Evento("a4", TiposDeEvento.TurnoFalhou, Hoje, detalhe: "limite-de-uso"),
            BaseDeTeste.Evento("a5", TiposDeEvento.FerramentaAcionada, Hoje, quantidade: 7, detalhe: "Bash"),
            BaseDeTeste.Evento("a6", TiposDeEvento.ObjetivoAvaliado, Hoje, valor: 80),
            BaseDeTeste.Evento("a7", TiposDeEvento.ObjetivoCumprido, Hoje));
        await Enviar(m2,
            BaseDeTeste.Evento("b1", TiposDeEvento.TurnoExecutado, Hoje, valor: 2000, detalhe: "kiro"),
            BaseDeTeste.Evento("b2", TiposDeEvento.ComandoDelegado, Hoje.AddMonths(-2), detalhe: "dotnet.exe")); // fora do período

        var consultas = (IServiceProvider sp) => sp.GetRequiredService<IConsultasDoPainel>();
        var todas = await _base.ComAsync(sp => consultas(sp).QuantidadeAsync(Outubro, null, default));
        var soM1 = await _base.ComAsync(sp => consultas(sp).QuantidadeAsync(Outubro, m1, default));
        var qualidade = await _base.ComAsync(sp => consultas(sp).QualidadeAsync(Outubro, m1, default));

        Assert.Equal((1L, 3L, 7L, 0L), (todas.Sessoes, todas.Turnos, todas.Ferramentas, todas.ComandosDelegados));
        Assert.Equal(2L, soM1.Turnos);
        Assert.Equal(31, soM1.SerieDiaria.Count); // todos os dias do período, com zero nos vazios
        Assert.Equal(1L, soM1.SerieDiaria.Single(d => d.Dia == new DateOnly(2026, 10, 2)).Turnos);
        Assert.Equal(0.5, qualidade.TaxaDeFalha);
        Assert.Equal(2000, qualidade.DuracaoMediaDoTurnoMs);
        Assert.Equal(80, qualidade.NotaMedia);
        Assert.Equal(2.0, qualidade.TurnosPorObjetivoCumprido);
        Assert.Null(qualidade.RazaoCumpridosRecusados); // nenhuma recusa: sem denominador

        var maquinas = await _base.ComAsync(sp => consultas(sp).MaquinasAsync(default));
        Assert.Equal(7L, maquinas.Single(m => m.Id == m1).Eventos);
        Assert.NotNull(maquinas.Single(m => m.Id == m1).UltimoEnvioEm);
    }

    [Fact]
    public async Task Log_de_eventos_pagina_do_mais_novo_para_o_mais_antigo()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        await Enviar(maquina, Enumerable.Range(1, 5)
            .Select(i => BaseDeTeste.Evento($"p{i}", TiposDeEvento.FerramentaAcionada, Hoje.AddMinutes(i), detalhe: "Read")).ToArray());

        var pagina = await _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().EventosAsync(Outubro, maquina, 2, 2, default));

        Assert.Equal(5, pagina.Total);
        Assert.Equal(new[] { "p3", "p2" }, pagina.Itens.Select(e => e.EventId));
        Assert.Equal("máquina m-1", pagina.Itens[0].Apelido);
    }

    [Fact]
    public async Task Expurgo_remove_so_o_que_passou_da_retencao_e_mantem_os_totais()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        var agora = _base.Relogio.Agora;
        await Enviar(maquina,
            BaseDeTeste.Evento("velho", TiposDeEvento.TurnoExecutado, agora.AddDays(-31), valor: 1000),
            BaseDeTeste.Evento("novo", TiposDeEvento.TurnoExecutado, agora.AddDays(-29), valor: 1000));
        var antes = await _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().QuantidadeAsync(new Periodo(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 3)), null, default));

        var apagados = await _base.ComAsync(sp => sp.GetRequiredService<IExpurgoDeEventos>().ExpurgarAsync(default));

        Assert.Equal(1, apagados);
        var restantes = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Eventos.Select(e => e.EventId).ToListAsync());
        Assert.Equal(new[] { "novo" }, restantes);
        var depois = await _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().QuantidadeAsync(new Periodo(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 3)), null, default));
        Assert.Equal(2L, antes.Turnos);
        Assert.Equal(antes.Turnos, depois.Turnos);
    }

    [Fact]
    public async Task Mesma_assinatura_vira_um_grupo_com_duas_ocorrencias_e_duas_maquinas_e_o_reenvio_nao_duplica()
    {
        var m1 = await _base.MaquinaAsync("m-1");
        var m2 = await _base.MaquinaAsync("m-2");
        var deM1 = BaseDeTeste.Excecao("x1", Hoje, "abc123", TraceDoDevKit);

        await Enviar(m1, deM1);
        await Enviar(m2, BaseDeTeste.Excecao("x2", Hoje.AddMinutes(5), "abc123", TraceDoDevKit));
        await Enviar(m1, deM1); // o reenvio

        var consultas = (IServiceProvider sp) => sp.GetRequiredService<IConsultasDoPainel>();
        var pagina = await _base.ComAsync(sp => consultas(sp).ErrosAsync(Outubro, null, 1, 20, default));
        var grupo = Assert.Single(pagina.Itens);
        Assert.Equal(("abc123", 2L, 2, EstadosDoGrupo.Novo), (grupo.Assinatura, grupo.Ocorrencias, grupo.Maquinas, grupo.Estado));
        Assert.Equal("System.InvalidOperationException", grupo.Tipo);

        var detalhe = (await _base.ComAsync(sp => consultas(sp).ErroAsync(grupo.Id, Outubro, default)))!;
        Assert.Equal(2, detalhe.Ocorrencias.Count);
        Assert.Contains("GitKit.Core.Services.Planejador.Escolher()", detalhe.Trace);
        Assert.Equal(new[] { "máquina m-1", "máquina m-2" }, detalhe.Maquinas);
        Assert.Equal(2L, Assert.Single(detalhe.PorDia).Quantidade);

        // Filtrado por máquina, conta só a dela; e o erro não entra na taxa de falha de turno.
        var soM1 = await _base.ComAsync(sp => consultas(sp).ErrosAsync(Outubro, m1, 1, 20, default));
        Assert.Equal((1L, 1), (soM1.Itens[0].Ocorrencias, soM1.Itens[0].Maquinas));
        Assert.Equal(0, (await _base.ComAsync(sp => consultas(sp).QualidadeAsync(Outubro, null, default))).TurnosComFalha);
    }

    [Fact]
    public async Task Trace_com_caminho_email_e_url_e_mascarado_de_novo_na_gravacao()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        var vazado = "System.IO.IOException: C:\\Users\\ana\\repo\\x.cs nao abriu (ana@cliente.com.br, https://cliente.dev/a?token=1)\n   at GitKit.X.Y() in \\\\servidor\\share\\y.cs:line 9";

        await Enviar(maquina, BaseDeTeste.Excecao("x1", Hoje, "def456", vazado));

        var trace = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().OcorrenciasDeErro.Select(o => o.Trace).SingleAsync());
        Assert.DoesNotContain("ana", trace);
        Assert.DoesNotContain("cliente", trace);
        Assert.DoesNotContain("servidor", trace);
        Assert.Contains("<caminho>", trace);
        Assert.Contains("GitKit.X.Y()", trace);
    }

    [Fact]
    public async Task Grupo_resolvido_regride_quando_volta_na_versao_da_correcao_e_nao_antes()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        await Enviar(maquina, BaseDeTeste.Excecao("x1", Hoje, "abc123", TraceDoDevKit));
        var id = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().GruposDeErro.Select(g => g.Id).SingleAsync());
        await _base.ComAsync(sp => sp.GetRequiredService<IReacaoAErros>().AlterarEstadoAsync(id, new AlterarEstadoDoGrupo(EstadosDoGrupo.Resolvido, "1.5.0"), default));

        await EnviarNaVersao(maquina, "1.4.9", BaseDeTeste.Excecao("x2", Hoje.AddMinutes(1), "abc123", TraceDoDevKit));
        var antes = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().GruposDeErro.AsNoTracking().SingleAsync());
        await EnviarNaVersao(maquina, "1.5.0", BaseDeTeste.Excecao("x3", Hoje.AddMinutes(2), "abc123", TraceDoDevKit));
        var depois = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().GruposDeErro.AsNoTracking().SingleAsync());

        Assert.Equal((EstadosDoGrupo.Resolvido, "1.5.0"), (antes.Estado, antes.ResolvidoNaVersao));
        Assert.Equal((EstadosDoGrupo.Regrediu, "1.5.0"), (depois.Estado, depois.UltimaVersao));
    }

    [Fact]
    public async Task Cada_grupo_guarda_so_as_ultimas_ocorrencias()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        await Enviar(maquina, Enumerable.Range(1, RegrasDeErro.OcorrenciasGuardadasPorGrupo + 5)
            .Select(i => BaseDeTeste.Excecao($"x{i}", Hoje.AddMinutes(i), "abc123", TraceDoDevKit)).ToArray());

        var guardadas = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().OcorrenciasDeErro.Select(o => o.EventId).ToListAsync());
        var grupo = Assert.Single((await _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().ErrosAsync(Outubro, null, 1, 20, default))).Itens);

        Assert.Equal(RegrasDeErro.OcorrenciasGuardadasPorGrupo, guardadas.Count);
        Assert.DoesNotContain("x1", guardadas); // a mais antiga saiu
        Assert.Equal(RegrasDeErro.OcorrenciasGuardadasPorGrupo + 5L, grupo.Ocorrencias); // a contagem não depende delas
    }

    [Fact]
    public async Task Expurgo_remove_as_ocorrencias_velhas_e_mantem_o_grupo_e_a_contagem()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        var agora = _base.Relogio.Agora;
        await Enviar(maquina,
            BaseDeTeste.Excecao("velha", agora.AddDays(-31), "abc123", TraceDoDevKit),
            BaseDeTeste.Excecao("nova", agora.AddDays(-1), "abc123", TraceDoDevKit));

        var apagadas = await _base.ComAsync(sp => sp.GetRequiredService<IExpurgoDeOcorrencias>().ExpurgarAsync(default));

        Assert.Equal(1, apagadas);
        Assert.Equal(new[] { "nova" }, await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().OcorrenciasDeErro.Select(o => o.EventId).ToListAsync()));
        var periodo = new Periodo(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 3));
        var grupo = Assert.Single((await _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().ErrosAsync(periodo, null, 1, 20, default))).Itens);
        Assert.Equal(2L, grupo.Ocorrencias);
    }

    [Fact]
    public async Task Maquinas_ativas_sao_as_com_evento_no_periodo_e_registradas_as_que_ja_existiam()
    {
        var m1 = await _base.MaquinaAsync("m-1");
        await _base.MaquinaAsync("m-2"); // registrada, sem evento
        var m3 = await _base.MaquinaAsync("m-3");
        await Enviar(m1, BaseDeTeste.Evento("a1", TiposDeEvento.SessaoIniciada, Hoje));
        await Enviar(m3, BaseDeTeste.Evento("c1", TiposDeEvento.SessaoIniciada, Hoje.AddMonths(-3))); // fora do período

        var todas = await _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().QuantidadeAsync(Outubro, null, default));
        var soM1 = await _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().QuantidadeAsync(Outubro, m1, default));

        Assert.Equal((1L, 3L), (todas.MaquinasAtivas, todas.MaquinasRegistradas));
        Assert.Equal((1L, 1L), (soM1.MaquinasAtivas, soM1.MaquinasRegistradas));
    }

    [Fact]
    public void Script_do_azure_sql_cria_os_grupos_e_as_ocorrencias_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("GruposDeErro.sql");

        foreach (var tabela in new[] { "GruposDeErro", "OcorrenciasDeErro" })
        {
            Assert.Contains($"IF OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
            foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
                Assert.Contains(coluna, script);
        }

        Assert.Contains("CREATE UNIQUE INDEX [IX_GruposDeErro_Assinatura]", script);
    }

    [Fact]
    public async Task Login_bloqueia_depois_de_falhas_seguidas()
    {
        var usuarios = (IServiceProvider sp) => sp.GetRequiredService<IUsuarios>();
        for (var i = 0; i < 5; i++)
            Assert.Equal(ResultadoDoLogin.CredencialInvalida, (await _base.ComAsync(sp => usuarios(sp).AutenticarAsync("admin", "errada", default))).Resultado);

        Assert.Equal(ResultadoDoLogin.Bloqueado, (await _base.ComAsync(sp => usuarios(sp).AutenticarAsync("admin", "senhaInicial2026", default))).Resultado);

        _base.Relogio.Agora = _base.Relogio.Agora.AddMinutes(16);
        Assert.Equal(ResultadoDoLogin.Ok, (await _base.ComAsync(sp => usuarios(sp).AutenticarAsync("admin", "senhaInicial2026", default))).Resultado);
    }
}
