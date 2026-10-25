using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>
/// O CONTRATO da renovação da licença (US #687, Rule de impacto "Licença"): o conjunto de <c>ContratoDaRenovacao/</c> — o
/// MESMO arquivo está no git.kit (<c>tests/GitKit.Core.Tests/Setup/ContratoDaRenovacao/</c>), onde o
/// <c>LicencaDoDevKit</c> decide com ele se chama a API. Aqui, a metade da API: a recusa (ou não) e a validade emitida.
/// </summary>
public sealed class ContratoDaRenovacaoTests
{
    private sealed record Admin(string Modo, DateTimeOffset? ValidaAte, DateTimeOffset? AlteradaEm);

    private sealed record Esperado(bool ClienteChama, bool ApiRenova, string? Recusa, DateTimeOffset? ValidaAteEmitida);

    private sealed record Caso(string Nome, DateTimeOffset EmitidaEm, DateTimeOffset ValidaAte, DateTimeOffset Agora, bool Forcar, Admin Admin, Esperado Esperado);

    private sealed record Contrato(int APartirDoDia, int ValidadeDias, IReadOnlyList<Caso> Casos);

    private static Contrato Ler()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            var arquivo = Path.Combine(pasta.FullName, "ContratoDaRenovacao", "casos.json");
            if (File.Exists(arquivo))
                return JsonSerializer.Deserialize<Contrato>(File.ReadAllText(arquivo), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }

        throw new DirectoryNotFoundException("ContratoDaRenovacao/ não encontrado.");
    }

    public static TheoryData<string> Nomes()
    {
        var dados = new TheoryData<string>();
        foreach (var caso in Ler().Casos)
            dados.Add(caso.Nome);
        return dados;
    }

    [Theory]
    [MemberData(nameof(Nomes))]
    public void A_api_decide_cada_caso_do_contrato(string nome)
    {
        var contrato = Ler();
        var caso = contrato.Casos.Single(c => c.Nome == nome);
        var empresa = new Empresa { Id = 1, Nome = "Acme", Assentos = 10 };
        var usuario = new Usuario { Id = 7, Login = "ana@acme.com", Papel = Papeis.Dev, EmpresaId = 1, Empresa = empresa };
        switch (caso.Admin.Modo)
        {
            case "data":
                usuario.LicencaValidaAteUtc = caso.Admin.ValidaAte!.Value.UtcDateTime;
                break;
            case "sem-prazo":
                usuario.LicencaSemPrazo = true;
                break;
            case "revogada":
                usuario.LicencaRevogadaEmUtc = caso.Admin.AlteradaEm!.Value.UtcDateTime;
                break;
        }

        usuario.LicencaAlteradaEmUtc = caso.Admin.AlteradaEm?.UtcDateTime;
        var atual = new ConteudoDaLicenca(LicencaAssinada.VersaoDoFormato, TiposDeLicenca.Assinatura, "maquina-1", usuario.Login, caso.EmitidaEm, caso.ValidaAte);

        var recusa = RegrasDeLicenca.Avaliar(usuario, empresa, atual, caso.Agora, contrato.APartirDoDia);

        Assert.Equal(caso.Esperado.ApiRenova, recusa == RecusaDaRenovacao.Nenhuma);
        if (caso.Esperado.Recusa is { } esperada)
            Assert.Equal(esperada, recusa.ToString());
        if (caso.Esperado.ValidaAteEmitida is { } validade)
            Assert.Equal(validade, RegrasDeLicenca.ValidadeEmitida(RegrasDeLicenca.Licenca(usuario, empresa), caso.Agora, contrato.ValidadeDias));
    }

    [Theory]
    [MemberData(nameof(Nomes))]
    public void A_janela_da_api_e_a_do_cliente(string nome)
    {
        // O cliente chama com login novo, ou na janela: a MESMA janela da API (sem a alteração do admin, que ele não vê).
        var contrato = Ler();
        var caso = contrato.Casos.Single(c => c.Nome == nome);

        Assert.Equal(caso.Esperado.ClienteChama, caso.Forcar || RegrasDeLicenca.NaJanela(caso.EmitidaEm, caso.ValidaAte, caso.Agora, contrato.APartirDoDia));
    }
}
