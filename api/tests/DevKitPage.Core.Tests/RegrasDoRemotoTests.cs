using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// O detector de dado sensível do controle remoto (<see cref="DadosSensiveis"/>): barra o token e o dado de cliente real
/// — e-mail, CPF, CNPJ (inclusive o alfanumérico de 2026) e telefone — SEM falso positivo nos textos do dev.kit: o
/// carimbo 20261102-095333 não é CNPJ, e o slug sprint-11-2026-2027 não é telefone.
/// </summary>
public sealed class DadosSensiveisTests
{
    [Theory]
    [InlineData("20261102-095333")]
    [InlineData("Captura 20261102-095333.png gravada")]
    [InlineData("20261102095333")]
    [InlineData("log-20261102-095333")]
    public void Data_e_hora_nao_e_cnpj(string texto)
    {
        Assert.False(DadosSensiveis.ContemCnpj(texto));
        Assert.Null(DadosSensiveis.Tipo(texto));
    }

    [Theory]
    [InlineData("sprint-11-2026-2027")]
    [InlineData("feature/dev/sprint-11-2026-2027")]
    [InlineData("Sprint 11 2026-2027")]
    [InlineData("release-21-3456-7890")]
    [InlineData("Item 11 de 12, onda 2026")]
    [InlineData("2026-11-03T09:53:33Z")]
    [InlineData("feature/dev/projeto-controle-remoto-v2-item-1")]
    public void Slug_data_e_faixa_de_anos_nao_sao_telefone(string texto)
    {
        Assert.False(DadosSensiveis.ContemTelefone(texto));
        Assert.Null(DadosSensiveis.Tipo(texto));
    }

    [Theory]
    [InlineData("Cliente 11.222.333/0001-81 pediu o ajuste")]
    [InlineData("CNPJ 11222333000181")]
    [InlineData("o fornecedor 12.ABC.345/01DE-35 (alfanumérico)")]
    public void Cnpj_real_e_barrado(string texto) => Assert.Equal(RemotoV1.SensivelCnpj, DadosSensiveis.Tipo(texto));

    [Theory]
    [InlineData("11.222.333/0001-80")]
    [InlineData("12ABC34501DE35")]
    [InlineData("11.111.111/1111-11")]
    public void Cnpj_com_dv_errado_ou_id_alfanumerico_solto_nao_e_cnpj(string texto) => Assert.False(DadosSensiveis.ContemCnpj(texto));

    [Theory]
    [InlineData("CPF 111.444.777-35 do responsável")]
    [InlineData("123.456.789-09")]
    [InlineData("cpf 11144477735")]
    public void Cpf_real_e_barrado(string texto) => Assert.Equal(RemotoV1.SensivelCpf, DadosSensiveis.Tipo(texto));

    [Theory]
    [InlineData("123.456.789-00")]
    [InlineData("111.111.111-11")]
    public void Cpf_com_dv_errado_nao_e_cpf(string texto) => Assert.False(DadosSensiveis.ContemCpf(texto));

    [Theory]
    [InlineData("Ligar para (11) 98765-4321")]
    [InlineData("+55 11 98765-4321")]
    [InlineData("+5511987654321")]
    [InlineData("contato 11 98765-4321")]
    [InlineData("whats 11987654321")]
    [InlineData("fixo (21) 2345-6789")]
    [InlineData("fixo 011 3456-7890")]
    [InlineData("Ligue 21 3456-7890 amanhã")]
    [InlineData("celular 98765-4321")]
    [InlineData("(11) 2026-2027")]
    public void Telefone_real_e_barrado(string texto) => Assert.Equal(RemotoV1.SensivelTelefone, DadosSensiveis.Tipo(texto));

    [Theory]
    [InlineData("(10) 2345-6789")]
    [InlineData("id 1234567890")]
    public void Ddd_inexistente_ou_id_de_dez_digitos_nao_e_telefone(string texto) => Assert.False(DadosSensiveis.ContemTelefone(texto));

    [Theory]
    [InlineData("Use o PAT ghp_abcdefghijklmnopqrstuvwxyz0123456789", RemotoV1.SensivelSegredo)]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJh", RemotoV1.SensivelSegredo)]
    [InlineData("password=Sup3rS3cret", RemotoV1.SensivelSegredo)]
    [InlineData("falar com ana.souza@cliente.com.br", RemotoV1.SensivelEmail)]
    public void Token_e_email_sao_barrados(string texto, string tipo) => Assert.Equal(tipo, DadosSensiveis.Tipo(texto));

    [Theory]
    [InlineData("API recebe heartbeat e retrato do dev.kit")]
    [InlineData("Qual fila usar: Storage Queue ou Service Bus?")]
    [InlineData("AB#812 · onda 2 · 3 itens")]
    [InlineData("exec-3f2b9c0e5d7a4b1f8e6d2c4a9b0f1e3d")]
    [InlineData("")]
    [InlineData(null)]
    public void Texto_comum_do_dev_kit_passa(string? texto) => Assert.Null(DadosSensiveis.Tipo(texto));
}

/// <summary>As regras do contrato v1 do controle remoto (<see cref="RegrasDoRemoto"/>): a forma do envio e o caminho do campo sensível.</summary>
public sealed class RegrasDoRemotoTests
{
    private static HeartbeatV1 Heartbeat() => new(1, "3f2b9c0e5d7a", "Notebook", "75", "servico", true, "ativo", false);

    private static RetratoV1 Retrato(string titulo = "Qual fila usar?") => new(
        1, "3f2b9c0e5d7a", DateTimeOffset.Parse("2026-11-03T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        [new ProjetoNoRetratoV1("sprint-11-2026-2027", "Sprint 11", "aprovado", 1, [new ItemNoRetratoV1(1, "API", "rodando", 1)])],
        [new FluxoNoRetratoV1("exec-20261102-095333", "Fluxo 6", "Implementação", "executando", "sprint-11-2026-2027", 1, "claude")],
        [new PendenciaNoRetratoV1("pergunta-1", "pergunta", titulo, "Storage Queue ou Service Bus?", ["Storage Queue", "Service Bus"])]);

    [Fact]
    public void O_envio_valido_passa_sem_campo_sensivel()
    {
        Assert.Equal(string.Empty, RegrasDoRemoto.Validar(Heartbeat()));
        Assert.Equal(string.Empty, RegrasDoRemoto.Validar(Retrato()));
        Assert.Empty(RegrasDoRemoto.CamposSensiveis(Heartbeat()));
        Assert.Empty(RegrasDoRemoto.CamposSensiveis(Retrato()));
    }

    [Fact]
    public void O_campo_sensivel_volta_com_o_caminho_e_o_tipo_sem_o_valor()
    {
        var retrato = Retrato("Ligar para o cliente 11.222.333/0001-81") with
        {
            Pendencias = [Retrato().Pendencias![0] with { Titulo = "Ligar para o cliente 11.222.333/0001-81", Opcoes = ["ok", "(11) 98765-4321"] }],
        };

        var campos = RegrasDoRemoto.CamposSensiveis(retrato);

        Assert.Equal([new CampoSensivelV1("pendencias[0].titulo", "cnpj"), new CampoSensivelV1("pendencias[0].opcoes[1]", "telefone")], campos);
        Assert.Equal([new CampoSensivelV1("nome", "email")], RegrasDoRemoto.CamposSensiveis(Heartbeat() with { Nome = "pc da ana@a.com" }));
    }

    [Theory]
    [InlineData("../fora")]
    [InlineData("")]
    [InlineData("com espaço")]
    public void O_id_da_maquina_fora_do_padrao_e_recusado(string maquinaId)
    {
        Assert.NotEqual(string.Empty, RegrasDoRemoto.Validar(Heartbeat() with { MaquinaId = maquinaId }));
        Assert.NotEqual(string.Empty, RegrasDoRemoto.Validar(Retrato() with { MaquinaId = maquinaId }));
    }

    [Fact]
    public void A_forma_fora_do_contrato_e_recusada()
    {
        Assert.Contains("origem", RegrasDoRemoto.Validar(Heartbeat() with { Origem = "celular" }));
        Assert.Contains("estado", RegrasDoRemoto.Validar(Heartbeat() with { EstadoDoAgente = "dormindo" }));
        Assert.Contains("projetos[0]", RegrasDoRemoto.Validar(Retrato() with { Projetos = [Retrato().Projetos![0] with { Situacao = "rodando" }] }));
        Assert.Contains("pendencias[0]", RegrasDoRemoto.Validar(Retrato() with { Pendencias = [Retrato().Pendencias![0] with { Tipo = "aviso" }] }));
        Assert.Contains("geradoEm", RegrasDoRemoto.Validar(Retrato() with { GeradoEm = default }));
        Assert.Contains("fluxos", RegrasDoRemoto.Validar(Retrato() with { Fluxos = Enumerable.Range(0, RemotoV1.MaximoDeFluxos + 1).Select(i => new FluxoNoRetratoV1($"f{i}", "F", "", "")).ToArray() }));
        Assert.Equal(string.Empty, RegrasDoRemoto.Validar(Heartbeat() with { EstadoDoAgente = null, Origem = " App " }));
    }

    [Fact]
    public void So_o_contrato_1_e_conhecido()
    {
        Assert.True(RegrasDoRemoto.ContratoConhecido(1));
        Assert.False(RegrasDoRemoto.ContratoConhecido(0));
        Assert.False(RegrasDoRemoto.ContratoConhecido(2));
    }
}
