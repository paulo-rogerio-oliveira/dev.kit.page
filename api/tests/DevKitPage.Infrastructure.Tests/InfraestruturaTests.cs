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

    private Task<BatchResultV1> Enviar(int maquina, params TelemetryEventV1[] eventos)
        => _base.ComAsync(sp => sp.GetRequiredService<IIngestaoDeTelemetria>()
            .RegistrarAsync(maquina, new TelemetryBatchV1("v1", "x", "1.4.0", eventos), default));

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
