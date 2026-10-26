using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras dos projetos compartilhados (US #693): o slug, as listas gravadas uma por linha, a validação do envio
/// e o plano — números únicos, dependências que existem e sem ciclo.
/// </summary>
public sealed class RegrasDosProjetosTests
{
    private static ItemDoProjetoV1 Item(int numero, params int[] dependencias)
        => new(numero, $"Item {numero}", string.Empty, "planejar-e-executar", ["https://github.com/acme/portal"], ["Core/Data"],
            dependencias, 0, 0, ProjetosV1.ItemPlanejado);

    private static EnvioDeProjetoV1 Envio(string slug = "projetos-no-orquestrador", int paralelismo = 3, string branch = "feature/dev/projeto-x",
        IReadOnlyList<string>? repositorios = null, IReadOnlyList<ItemDoProjetoV1>? itens = null, string? situacao = null)
        => new(slug, "Projetos no Orquestrador", null, null, repositorios ?? ["https://github.com/acme/portal"], branch, false,
            "Criar o recurso de projetos.", [], true, paralelismo, "feature/dev/semana5", situacao, null, itens ?? []);

    [Theory]
    [InlineData("projetos-no-orquestrador", true)]
    [InlineData("semana5", true)]
    [InlineData("Projetos", false)]
    [InlineData("-comeca-com-hifen", false)]
    [InlineData("com espaco", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void O_slug_usa_minusculas_digitos_e_hifen(string? slug, bool valido) => Assert.Equal(valido, RegrasDosProjetos.SlugValido(slug));

    [Fact]
    public void A_lista_vira_uma_por_linha_sem_vazios_nem_repetidos_e_volta()
    {
        var linhas = RegrasDosProjetos.Linhas([" a ", "", "b", "a"]);

        Assert.Equal("a\nb", linhas);
        Assert.Equal(["a", "b"], RegrasDosProjetos.Lista(linhas));
        Assert.Equal([1, 3], RegrasDosProjetos.Dependencias("1, x,3,0"));
    }

    [Fact]
    public void O_envio_completo_vale()
        => Assert.Equal(string.Empty, RegrasDosProjetos.Validar(Envio(itens: [Item(1), Item(2, 1), Item(3, 1, 2)])));

    [Theory]
    [InlineData("Maiusculas", 3, "feature/dev/x", "slug")]
    [InlineData("ok", 0, "feature/dev/x", "paralelismo")]
    [InlineData("ok", 9, "feature/dev/x", "paralelismo")]
    [InlineData("ok", 3, "com espaco", "branch")]
    [InlineData("ok", 3, "", "branch")]
    public void O_envio_invalido_diz_o_que_falta(string slug, int paralelismo, string branch, string trecho)
        => Assert.Contains(trecho, RegrasDosProjetos.Validar(Envio(slug, paralelismo, branch)), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Projeto_sem_repositorio_ou_com_situacao_desconhecida_e_recusado()
    {
        Assert.Contains("repositório", RegrasDosProjetos.Validar(Envio(repositorios: [" "])));
        Assert.Contains("situação", RegrasDosProjetos.Validar(Envio(situacao: "inventada")));
    }

    [Fact]
    public void Dependencia_circular_e_recusada_com_o_item_que_fecha_o_ciclo()
    {
        var itens = new[] { Item(1, 3), Item(2, 1), Item(3, 2) };

        Assert.NotNull(RegrasDosProjetos.ItemEmCiclo(itens));
        Assert.Contains("ciclo", RegrasDosProjetos.Validar(Envio(itens: itens)));
    }

    [Fact]
    public void Dependencia_de_si_mesmo_ou_de_item_fora_do_plano_e_numero_repetido_sao_recusados()
    {
        Assert.Contains("si mesmo", RegrasDosProjetos.ValidarItens([Item(1, 1)]));
        Assert.Contains("não está no plano", RegrasDosProjetos.ValidarItens([Item(1, 7)]));
        Assert.Contains("único", RegrasDosProjetos.ValidarItens([Item(1), Item(1)]));
    }

    [Fact]
    public void Plano_sem_ciclo_nao_aponta_item()
        => Assert.Null(RegrasDosProjetos.ItemEmCiclo([Item(1), Item(2, 1), Item(3, 1), Item(4, 2, 3)]));
}
