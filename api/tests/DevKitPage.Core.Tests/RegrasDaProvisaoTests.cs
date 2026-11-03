using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras da provisão (projeto controle-remoto, item 4): os nomes únicos e repetíveis, a liberação do admin e a do
/// plano, o pedido, a espera entre as tentativas, o que cada estado aceita e a mensagem que vai para a auditoria.
/// </summary>
public sealed class RegrasDaProvisaoTests
{
    [Fact]
    public void O_nome_da_conta_e_o_mesmo_a_cada_repeticao_e_segue_a_regra_do_azure()
    {
        var nome = RegrasDaProvisao.NomeDaConta("prod", 42, 1);

        Assert.Equal(nome, RegrasDaProvisao.NomeDaConta("prod", 42, 1));
        Assert.Equal(nome, RegrasDaProvisao.NomeDaConta(" PROD ", 42, 1));
        Assert.Equal(18, nome.Length);
        Assert.StartsWith("dk", nome, StringComparison.Ordinal);
        Assert.True(RegrasDaProvisao.NomeDaContaValido(nome));
    }

    [Fact]
    public void O_nome_da_conta_muda_com_o_ambiente_o_usuario_e_a_geracao()
    {
        string[] nomes =
        [
            RegrasDaProvisao.NomeDaConta("prod", 42, 1),
            RegrasDaProvisao.NomeDaConta("dev", 42, 1),
            RegrasDaProvisao.NomeDaConta("prod", 43, 1),
            RegrasDaProvisao.NomeDaConta("prod", 42, 2),
        ];

        Assert.Equal(nomes.Length, nomes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(nomes, n => Assert.True(RegrasDaProvisao.NomeDaContaValido(n)));
    }

    [Theory]
    [InlineData("rg-devkit", 7, "rg-devkit-u7")]
    [InlineData(" RG-DevKit ", 7, "rg-devkit-u7")]
    [InlineData("", 7, "rg-devkit-u7")]
    [InlineData("grupo com espaço", 7, "rg-devkit-u7")]
    [InlineData("rg-prod.devkit", 1234, "rg-prod.devkit-u1234")]
    public void O_grupo_de_recursos_e_um_por_usuario(string prefixo, int usuario, string esperado)
        => Assert.Equal(esperado, RegrasDaProvisao.GrupoDeRecursos(prefixo, usuario));

    [Theory]
    [InlineData("comandos", true)]
    [InlineData("fila-do-usuario-1", true)]
    [InlineData("ab", false)]
    [InlineData("-comandos", false)]
    [InlineData("comandos-", false)]
    [InlineData("co--mandos", false)]
    [InlineData("Comandos", false)]
    public void O_nome_da_fila_segue_a_regra_do_azure(string nome, bool valido)
        => Assert.Equal(valido, RegrasDaProvisao.NomeDaFilaValido(nome));

    [Theory]
    [InlineData("abc", true)]
    [InlineData("dk0123456789abcdefghijkl", true)]
    [InlineData("ab", false)]
    [InlineData("dk0123456789abcdefghijklm", false)]
    [InlineData("dk-conta", false)]
    [InlineData("DKCONTA", false)]
    public void O_nome_da_conta_segue_a_regra_do_azure(string nome, bool valido)
        => Assert.Equal(valido, RegrasDaProvisao.NomeDaContaValido(nome));

    [Fact]
    public void A_liberacao_vem_do_admin_ou_do_plano_e_sem_nenhuma_e_nula()
    {
        Assert.Equal(ProvisaoV1.OrigemUsuario, RegrasDaProvisao.Origem(true, null, []));
        Assert.Equal(ProvisaoV1.OrigemUsuario, RegrasDaProvisao.Origem(true, "Pro", ["Pro"]));
        Assert.Equal(ProvisaoV1.OrigemPlano, RegrasDaProvisao.Origem(false, "pro", [" Pro "]));
        Assert.Null(RegrasDaProvisao.Origem(false, "Empresarial", ["Pro"]));
        Assert.Null(RegrasDaProvisao.Origem(false, null, ["Pro"]));
        Assert.Null(RegrasDaProvisao.Origem(false, "", [""]));
        // Hoje nenhum plano libera: só o admin, usuário a usuário.
        Assert.Null(RegrasDaProvisao.Origem(false, "Empresarial", new OpcoesDaProvisao().PlanosQueLiberam));
    }

    [Fact]
    public void O_pedido_aceita_so_as_regioes_configuradas_e_a_chave_curta()
    {
        IReadOnlyList<string> regioes = ["brazilsouth", "eastus2"];

        Assert.Null(RegrasDaProvisao.Validar(new PedidoDeFilaV1("brazilsouth"), regioes));
        Assert.Null(RegrasDaProvisao.Validar(new PedidoDeFilaV1(" BrazilSouth ", "pedido-1_a"), regioes));
        Assert.Contains("Região não aceita", RegrasDaProvisao.Validar(new PedidoDeFilaV1("westeurope"), regioes));
        Assert.Contains("Região não aceita", RegrasDaProvisao.Validar(new PedidoDeFilaV1(""), regioes));
        Assert.NotNull(RegrasDaProvisao.Validar(null, regioes));
        Assert.Contains("chave do cliente", RegrasDaProvisao.Validar(new PedidoDeFilaV1("brazilsouth", "com espaço"), regioes));
        Assert.Contains("chave do cliente", RegrasDaProvisao.Validar(new PedidoDeFilaV1("brazilsouth", new string('a', 65)), regioes));
    }

    [Fact]
    public void As_regioes_vazias_na_configuracao_voltam_ao_padrao_e_a_sugerida_precisa_ser_aceita()
    {
        Assert.Equal(OpcoesDaProvisao.RegioesPadrao, new OpcoesDaProvisao().RegioesAceitas());

        var opcoes = new OpcoesDaProvisao { Regioes = [" EastUS2 ", "eastus2", "westeurope"], RegiaoPadrao = "brazilsouth" };
        Assert.Equal(["eastus2", "westeurope"], opcoes.RegioesAceitas());
        Assert.Equal("eastus2", opcoes.RegiaoSugerida());
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(6, 960)]
    [InlineData(7, 1800)]
    [InlineData(60, 1800)]
    public void A_espera_dobra_a_cada_falha_ate_o_teto(int falhas, int segundos)
        => Assert.Equal(TimeSpan.FromSeconds(segundos), RegrasDaProvisao.Espera(falhas, 30, 1800));

    [Fact]
    public void Cada_estado_aceita_o_que_pode()
    {
        Assert.True(RegrasDaProvisao.PodeRepetir(ProvisaoV1.Falhou));
        Assert.All([ProvisaoV1.Pedido, ProvisaoV1.EmProvisao, ProvisaoV1.Pronto, ProvisaoV1.Removido], e => Assert.False(RegrasDaProvisao.PodeRepetir(e)));

        Assert.NotNull(RegrasDaProvisao.PorQueNaoRemove(ProvisaoV1.OperacaoCriar, ProvisaoV1.Pedido));
        Assert.NotNull(RegrasDaProvisao.PorQueNaoRemove(ProvisaoV1.OperacaoCriar, ProvisaoV1.EmProvisao));
        Assert.Null(RegrasDaProvisao.PorQueNaoRemove(ProvisaoV1.OperacaoCriar, ProvisaoV1.Pronto));
        Assert.Null(RegrasDaProvisao.PorQueNaoRemove(ProvisaoV1.OperacaoCriar, ProvisaoV1.Falhou));
        Assert.Null(RegrasDaProvisao.PorQueNaoRemove(ProvisaoV1.OperacaoRemover, ProvisaoV1.Falhou));

        Assert.False(RegrasDaProvisao.Ativo(ProvisaoV1.Removido));
        Assert.True(RegrasDaProvisao.Ativo(ProvisaoV1.Falhou));
    }

    [Fact]
    public void A_mensagem_da_auditoria_nao_leva_segredo_nem_o_id_da_assinatura_e_fica_numa_linha()
    {
        var texto = "AuthorizationFailed: o cliente 'f1e2d3c4-b5a6-4789-9012-3456789abcde' não pode em "
            + "/subscriptions/0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0/resourcegroups/rg-devkit-u7\n"
            + "Authorization: Bearer eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJh";

        var segura = RegrasDaProvisao.MensagemSegura(texto);

        Assert.DoesNotContain("0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0", segura, StringComparison.Ordinal);
        Assert.DoesNotContain("f1e2d3c4-b5a6-4789-9012-3456789abcde", segura, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiJ9", segura, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', segura);
        Assert.StartsWith("AuthorizationFailed", segura, StringComparison.Ordinal);
        Assert.True(RegrasDaProvisao.MensagemSegura(new string('x', 2000)).Length <= ProvisaoV1.TamanhoMaximoDaMensagem);
    }

    [Fact]
    public void As_etiquetas_dizem_de_quem_e_o_custo()
    {
        var etiquetas = RegrasDaProvisao.Etiquetas(" PROD ", 42, 9);

        Assert.Equal("prod", etiquetas["devkit-ambiente"]);
        Assert.Equal("42", etiquetas["devkit-usuario"]);
        Assert.Equal("9", etiquetas["devkit-recurso"]);
    }
}
