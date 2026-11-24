using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

public sealed class RegrasDaFinalizacaoTests
{
    private static EnvioDeProjetoV1 Projeto() => new("projeto", "Projeto", "usuario", "", ["https://github.com/acme/a"],
        "feature/projeto", false, "", [], false, 1, "develop", "aprovado", null,
        [new(1, "Item", "", "item", [], [], [], 1, 0, "integrado")])
    {
        PoliticaDeFinalizacao = new("projeto", "finalizacao-unica", 2, "1", DateTimeOffset.UtcNow, "adoção")
        { IdentidadeDoFluxo = "fluxo:final", RevisaoDoFluxo = "rev", ModeloDaFinalizacao = new("claude", "sonnet") },
    };
    private static ReciboDaFinalizacaoV1 Recibo() => new(1, "exec-final", 3, "projeto", "feature/projeto", "develop", 2,
        "fluxo:final", "rev", "claude", "sonnet", new Dictionary<string, string>
        { [RegrasDaFinalizacao.IdDoRepositorio("https://github.com/acme/a")] = new('a', 40) });

    [Fact]
    public void Recibo_da_composicao_atual_e_aceito_e_origens_equivalentes_tem_identidade_opaca()
    {
        Assert.Empty(RegrasDaFinalizacao.Conferir(Recibo(), "exec-final", Projeto()));
        var id = RegrasDaFinalizacao.IdDoRepositorio("https://github.com/acme/a");
        Assert.Equal(id, RegrasDaFinalizacao.IdDoRepositorio("git@github.com:acme/a.git"));
        Assert.Equal(id, RegrasDaFinalizacao.IdDoRepositorio("ssh://git@github.com/acme/a.git"));
        Assert.Equal(id, RegrasDaFinalizacao.IdDoRepositorio("https://github.com/Acme/A.git/"));
        Assert.Equal(64, id.Length);
    }

    [Theory]
    [InlineData("ausente")]
    [InlineData("cliente-antigo")]
    [InlineData("sessao")]
    [InlineData("execucao")]
    [InlineData("projeto")]
    [InlineData("politica")]
    [InlineData("fluxo")]
    [InlineData("revisao-fluxo")]
    [InlineData("modelo")]
    [InlineData("branch")]
    [InlineData("destino")]
    [InlineData("repositorio")]
    [InlineData("sha")]
    public void Recusa_cliente_antigo_declaracao_sem_recibo_ou_composicao_divergente(string caso)
    {
        ReciboDaFinalizacaoV1? recibo = Recibo();
        var projeto = Projeto();
        recibo = caso switch
        {
            "ausente" => null,
            "sessao" => recibo with { Sessao = "outra" },
            "execucao" => recibo with { Execucao = 0 },
            "projeto" => recibo with { Projeto = "outro" },
            "politica" => recibo with { RevisaoDaPolitica = 1 },
            "fluxo" => recibo with { Fluxo = "outro" },
            "revisao-fluxo" => recibo with { RevisaoDoFluxo = "antiga" },
            "modelo" => recibo with { Modelo = "opus" },
            "branch" => recibo with { Branch = "outro" },
            "destino" => recibo with { Destino = "main" },
            "repositorio" => recibo with { Shas = new Dictionary<string, string> { [new('b', 64)] = new('a', 40) } },
            "sha" => recibo with { Shas = new Dictionary<string, string> { [recibo.Shas.Keys.Single()] = "desconhecido" } },
            _ => recibo,
        };
        if (caso == "cliente-antigo") projeto = projeto with { PoliticaDeFinalizacao = null };
        Assert.NotEmpty(RegrasDaFinalizacao.Conferir(recibo, "exec-final", projeto));
    }
}
