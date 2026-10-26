using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>
/// A licença por usuário que o admin define (US #687): o tipo e a validade que valem (a do admin antes do contrato da
/// empresa), a situação da coluna Licença, a validade nova de cada modo (somar à atual, até uma data, sem prazo), a
/// validação com o motivo e a validade emitida — os 30 dias de sempre, sem passar da do admin.
/// </summary>
public sealed class LicencaDoUsuarioTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static Usuario Dev(Empresa? empresa = null) => new() { Id = 2, Login = "ana", Papel = Papeis.Dev, EmpresaId = empresa?.Id, Empresa = empresa };

    [Fact]
    public void A_conta_de_administracao_nao_tem_prazo()
    {
        var licenca = RegrasDeLicenca.Licenca(new Usuario { EhAdmin = true, Papel = Papeis.Admin }, null);

        Assert.Equal((TiposDaLicencaDoUsuario.Admin, true, (DateTimeOffset?)null), (licenca.Tipo, licenca.SemPrazo, licenca.ValidaAte));
        Assert.Equal(SituacoesDaLicenca.SemPrazo, RegrasDeLicenca.Situacao(licenca, Agora).Situacao);
    }

    [Fact]
    public void A_validade_do_admin_vale_antes_do_contrato_da_empresa_e_o_tipo_deriva_da_empresa()
    {
        var acme = new Empresa { Id = 1, ContratoAteUtc = new DateTime(2027, 3, 16, 23, 59, 59) };
        var individual = new Empresa { Id = 2, Individual = true };
        var dev = Dev(acme);

        Assert.Equal((TiposDaLicencaDoUsuario.Empresa, new DateTimeOffset(2027, 3, 16, 23, 59, 59, TimeSpan.Zero), false),
            (RegrasDeLicenca.Licenca(dev, acme).Tipo, RegrasDeLicenca.Licenca(dev, acme).ValidaAte, RegrasDeLicenca.Licenca(dev, acme).DefinidaPeloAdmin));
        Assert.Equal(TiposDaLicencaDoUsuario.Individual, RegrasDeLicenca.Licenca(Dev(individual), individual).Tipo);
        Assert.Equal(TiposDaLicencaDoUsuario.Trial, RegrasDeLicenca.Licenca(Dev(), null).Tipo);

        dev.LicencaValidaAteUtc = new DateTime(2026, 10, 28, 23, 59, 59);
        dev.LicencaTipo = TiposDaLicencaDoUsuario.Trial;
        var doAdmin = RegrasDeLicenca.Licenca(dev, acme);
        Assert.Equal((TiposDaLicencaDoUsuario.Trial, new DateTimeOffset(2026, 10, 28, 23, 59, 59, TimeSpan.Zero), true), (doAdmin.Tipo, doAdmin.ValidaAte, doAdmin.DefinidaPeloAdmin));
    }

    [Theory]
    [InlineData(2027, 3, 16, SituacoesDaLicenca.Valida, 142)]
    [InlineData(2026, 10, 28, SituacoesDaLicenca.Vencendo, 3)]
    [InlineData(2026, 11, 1, SituacoesDaLicenca.Vencendo, 7)]
    [InlineData(2026, 11, 2, SituacoesDaLicenca.Valida, 8)]
    [InlineData(2026, 10, 25, SituacoesDaLicenca.Vencendo, 0)]
    [InlineData(2026, 10, 21, SituacoesDaLicenca.Vencida, -4)]
    public void A_situacao_e_os_dias_contam_por_data(int ano, int mes, int dia, string situacao, int dias)
    {
        var licenca = new LicencaDoUsuario(TiposDaLicencaDoUsuario.Empresa, RegrasDeLicenca.FimDoDia(new DateOnly(ano, mes, dia)), false, false, false);

        Assert.Equal((situacao, (int?)dias), RegrasDeLicenca.Situacao(licenca, Agora));
    }

    [Fact]
    public void Revogada_sem_prazo_e_automatica_nao_tem_dias()
    {
        Assert.Equal((SituacoesDaLicenca.Revogada, (int?)null), RegrasDeLicenca.Situacao(new("empresa", null, false, true, true), Agora));
        Assert.Equal((SituacoesDaLicenca.Automatica, (int?)null), RegrasDeLicenca.Situacao(new("empresa", null, false, false, false), Agora));
    }

    [Fact]
    public void Somar_parte_da_validade_atual_ou_de_hoje_quando_ela_passou()
    {
        var atual = new LicencaDoUsuario(TiposDaLicencaDoUsuario.Trial, RegrasDeLicenca.FimDoDia(new DateOnly(2026, 10, 28)), false, false, true);
        var vencida = atual with { ValidaAte = RegrasDeLicenca.FimDoDia(new DateOnly(2026, 10, 1)) };
        var somar30 = new DefinicaoDeLicencaV1(ModosDaValidade.Somar, Dias: 30);

        Assert.Equal(RegrasDeLicenca.FimDoDia(new DateOnly(2026, 11, 27)), RegrasDeLicenca.NovaValidade(somar30, atual, Agora).ValidaAte);
        Assert.Equal(RegrasDeLicenca.FimDoDia(new DateOnly(2026, 11, 24)), RegrasDeLicenca.NovaValidade(somar30, vencida, Agora).ValidaAte);
        Assert.Equal(RegrasDeLicenca.FimDoDia(new DateOnly(2026, 11, 24)), RegrasDeLicenca.NovaValidade(somar30, atual with { Revogada = true }, Agora).ValidaAte);
        Assert.Equal(33, RegrasDeLicenca.DiasAte(RegrasDeLicenca.NovaValidade(somar30, atual, Agora).ValidaAte!.Value, Agora));
    }

    [Fact]
    public void Ate_uma_data_e_sem_prazo()
    {
        var atual = new LicencaDoUsuario(TiposDaLicencaDoUsuario.Trial, null, false, false, false);

        Assert.Equal((RegrasDeLicenca.FimDoDia(new DateOnly(2027, 1, 31)), false),
            RegrasDeLicenca.NovaValidade(new DefinicaoDeLicencaV1(ModosDaValidade.AteData, Ate: new DateOnly(2027, 1, 31)), atual, Agora));
        Assert.Equal(((DateTimeOffset?)null, true), RegrasDeLicenca.NovaValidade(new DefinicaoDeLicencaV1(ModosDaValidade.SemPrazo), atual, Agora));
    }

    [Theory]
    [InlineData("outro", 30, null, null, "motivo", "Modo inválido")]
    [InlineData(ModosDaValidade.Somar, 0, null, null, "motivo", "quantos dias")]
    [InlineData(ModosDaValidade.Somar, 4000, null, null, "motivo", "quantos dias")]
    [InlineData(ModosDaValidade.AteData, null, null, null, "motivo", "data-limite")]
    [InlineData(ModosDaValidade.AteData, null, "2026-10-24", null, "motivo", "já passou")]
    [InlineData(ModosDaValidade.Somar, 30, null, "vitalicia", "motivo", "Tipo inválido")]
    [InlineData(ModosDaValidade.Somar, 30, null, null, "  ", "motivo")]
    public void Validacao_da_definicao(string modo, int? dias, string? ate, string? tipo, string motivo, string trecho)
    {
        var definicao = new DefinicaoDeLicencaV1(modo, dias, ate is null ? null : DateOnly.Parse(ate, System.Globalization.CultureInfo.InvariantCulture), tipo, motivo);

        Assert.Contains(trecho, RegrasDeLicenca.ValidarDefinicao(definicao, exigirMotivo: true, Agora));
    }

    [Fact]
    public void A_previa_nao_exige_o_motivo_e_a_aplicacao_sim()
    {
        var semMotivo = new DefinicaoDeLicencaV1(ModosDaValidade.Somar, 30, Tipo: TiposDaLicencaDoUsuario.Individual);

        Assert.Empty(RegrasDeLicenca.ValidarDefinicao(semMotivo, exigirMotivo: false, Agora));
        Assert.NotEmpty(RegrasDeLicenca.ValidarDefinicao(semMotivo, exigirMotivo: true, Agora));
        Assert.Empty(RegrasDeLicenca.ValidarDefinicao(new DefinicaoDeLicencaV1(ModosDaValidade.AteData, Ate: new DateOnly(2026, 10, 25), Motivo: "hoje"), true, Agora));
        Assert.Contains("até 300", RegrasDeLicenca.ValidarMotivo(new string('x', 301), exigir: true));
    }

    [Fact]
    public void A_validade_emitida_e_a_de_sempre_sem_passar_da_do_admin()
    {
        var curta = new LicencaDoUsuario(TiposDaLicencaDoUsuario.Trial, RegrasDeLicenca.FimDoDia(new DateOnly(2026, 10, 28)), false, false, true);
        var longa = curta with { ValidaAte = RegrasDeLicenca.FimDoDia(new DateOnly(2027, 10, 28)) };

        Assert.Equal(curta.ValidaAte, RegrasDeLicenca.ValidadeEmitida(curta, Agora, 30));
        Assert.Equal(Agora.AddDays(30), RegrasDeLicenca.ValidadeEmitida(longa, Agora, 30));
        Assert.Equal(Agora.AddDays(30), RegrasDeLicenca.ValidadeEmitida(curta with { ValidaAte = null, SemPrazo = true }, Agora, 30));
    }

    [Fact]
    public void O_usuario_sem_empresa_e_o_do_teste_e_renova_ate_o_fim_dele()
    {
        // Cadastrado em 20/10: o teste vai até o fim de 03/11 (14 dias).
        var trial = Dev();
        trial.CriadoEmUtc = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        var licenca = RegrasDeLicenca.Licenca(trial, null);

        Assert.Equal((TiposDaLicencaDoUsuario.Trial, RegrasDeLicenca.FimDoDia(new DateOnly(2026, 11, 3)), false), (licenca.Tipo, licenca.ValidaAte, licenca.DefinidaPeloAdmin));
        Assert.Equal((SituacoesDaLicenca.Valida, (int?)9), RegrasDeLicenca.Situacao(licenca, Agora));
        Assert.Equal(RecusaDaRenovacao.Nenhuma, RegrasDeLicenca.Avaliar(trial, null, null, Agora, 7));
        // A licença emitida não passa do fim do teste — e os dias do teste vêm da configuração.
        Assert.Equal(RegrasDeLicenca.FimDoDia(new DateOnly(2026, 11, 3)), RegrasDeLicenca.ValidadeEmitida(licenca, Agora, 30));
        Assert.Equal(RegrasDeLicenca.FimDoDia(new DateOnly(2026, 10, 27)), RegrasDeLicenca.Licenca(trial, null, diasDoTeste: 7).ValidaAte);

        Assert.Equal(RecusaDaRenovacao.TesteEncerrado, RegrasDeLicenca.Avaliar(trial, null, null, new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), 7));
        Assert.Equal(RecusaDaRenovacao.TesteEncerrado, RegrasDeLicenca.Avaliar(trial, null, null, Agora, 7, diasDoTeste: 3));
    }

    [Fact]
    public void A_licenca_definida_pelo_admin_renova_sem_empresa_e_a_vencida_ou_revogada_nao()
    {
        var solta = Dev();

        Assert.Equal(RecusaDaRenovacao.TesteEncerrado, RegrasDeLicenca.Avaliar(solta, null, null, Agora, 7)); // cadastro "nunca": o teste passou
        solta.LicencaSemPrazo = true;
        Assert.Equal(RecusaDaRenovacao.Nenhuma, RegrasDeLicenca.Avaliar(solta, null, null, Agora, 7));
        solta.LicencaRevogadaEmUtc = Agora.UtcDateTime;
        Assert.Equal(RecusaDaRenovacao.LicencaRevogada, RegrasDeLicenca.Avaliar(solta, null, null, Agora, 7));

        var acme = new Empresa { Id = 1, ContratoAteUtc = Agora.UtcDateTime.AddDays(-1) };
        Assert.Equal(RecusaDaRenovacao.LicencaVencida, RegrasDeLicenca.Avaliar(Dev(acme), acme, null, Agora, 7));
    }

    [Fact]
    public void O_contrato_editado_depois_da_emissao_abre_a_janela()
    {
        var acme = new Empresa { Id = 1 };
        var atual = new ConteudoDaLicenca(LicencaAssinada.VersaoDoFormato, TiposDeLicenca.Assinatura, "m", "ana", Agora.AddDays(-2), Agora.AddDays(28));

        Assert.Equal(RecusaDaRenovacao.CedoDemais, RegrasDeLicenca.Avaliar(Dev(acme), acme, atual, Agora, 7));
        acme.AlteradaEmUtc = Agora.UtcDateTime.AddDays(-1);
        Assert.Equal(RecusaDaRenovacao.Nenhuma, RegrasDeLicenca.Avaliar(Dev(acme), acme, atual, Agora, 7));
    }
}
