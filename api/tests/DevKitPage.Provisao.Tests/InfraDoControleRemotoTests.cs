using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DevKitPage.Provisao.Tests;

/// <summary>
/// O script de infraestrutura (<c>api/scripts/azure/infra-controle-remoto.sh</c>) na SIMULAÇÃO — nada vai ao Azure: tudo da
/// provisão fica no grupo fixo dos clientes (as funções atribuíveis só nele, as atribuições nele, nenhuma no escopo da
/// assinatura), o worker sem <c>resourceGroups/write</c>, nenhum grupo criado, e o <c>Provisao__Grupo</c> gravado.
/// </summary>
public sealed class InfraDoControleRemotoTests
{
    private const string Assinatura = "82ba9ad4-40bb-4bfe-91e3-1a4ba1edf95a";

    private static string Script()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            var script = Path.Combine(pasta.FullName, "scripts", "azure", "infra-controle-remoto.sh");
            if (File.Exists(script))
                return script.Replace('\\', '/');
        }
        throw new FileNotFoundException("api/scripts/azure/infra-controle-remoto.sh não foi achado acima da pasta dos testes.");
    }

    /// <summary>O bash: o do Git no Windows (o do System32 é o do WSL), o do PATH no CI.</summary>
    private static string Bash()
    {
        if (!OperatingSystem.IsWindows())
            return "bash";
        var git = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        Assert.True(File.Exists(git), $"O teste do script precisa do Git Bash em {git}.");
        return git;
    }

    private static async Task<(int Saida, string Texto)> RodarAsync(params string[] argumentos)
    {
        var inicio = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        inicio.ArgumentList.Add(Script());
        foreach (var argumento in argumentos)
            inicio.ArgumentList.Add(argumento);
        using var processo = Process.Start(inicio)!;
        var saida = processo.StandardOutput.ReadToEndAsync();
        var erro = processo.StandardError.ReadToEndAsync();
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await processo.WaitForExitAsync(limite.Token);
        return (processo.ExitCode, await saida + await erro);
    }

    [Fact]
    public async Task A_simulacao_poe_tudo_no_grupo_dos_clientes_e_nada_no_escopo_da_assinatura()
    {
        var (saida, texto) = await RodarAsync("--dry-run", "--assinatura", Assinatura, "--grupo", "rg-devkit-app", "--app", "devkit",
            "--grupo-clientes", "devkit-clients", "--conta-keyring", "stdevkitkeyring", "--cofre", "kv-devkit");
        var linhas = texto.Split('\n', StringSplitOptions.TrimEntries);
        var grupo = $"/subscriptions/{Assinatura}/resourceGroups/devkit-clients";

        Assert.True(saida == 0, texto);
        // As duas funções: atribuíveis só no grupo dos clientes, criadas e atribuídas nele.
        var escopos = linhas.Where(l => l.StartsWith("\"AssignableScopes\"", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, escopos.Length);
        Assert.All(escopos, l => Assert.Equal($"\"AssignableScopes\": [\"{grupo}\"]", l));
        foreach (var papel in new[] { "devkit-provisao-worker", "devkit-api-chave-da-fila" })
            Assert.Contains(linhas, l => l.StartsWith("+ az role assignment create", StringComparison.Ordinal)
                && l.Contains($"--role {papel} --scope {grupo}", StringComparison.Ordinal));
        // Nenhuma atribuição nem consulta no escopo da assinatura inteira.
        Assert.DoesNotContain(linhas, l => Regex.IsMatch(l, $@"--scope /subscriptions/{Assinatura}\s*$"));
        // O worker não cria grupo: sem resourceGroups/write na função, e nenhum group create.
        Assert.DoesNotContain("resourceGroups/write", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(linhas, l => l.StartsWith("+ az group create", StringComparison.Ordinal));
        // O worker e a API recebem o grupo fixo.
        Assert.Contains(linhas, l => l.StartsWith("+ az webapp config appsettings set", StringComparison.Ordinal)
            && l.Contains("Provisao__Grupo=devkit-clients", StringComparison.Ordinal));
        Assert.Contains(linhas, l => l.StartsWith("? az group show -n devkit-clients", StringComparison.Ordinal));
    }

    /// <summary>
    /// O arquivo da função vai ao az com o caminho que ELE lê: no Git Bash (Windows, com cygpath) o caminho do Windows — o az
    /// do Windows não enxerga o /tmp do MSYS ("Failed to parse string as JSON: @/tmp/...") —; no Linux, o caminho como está.
    /// </summary>
    [Fact]
    public async Task O_arquivo_da_funcao_vai_ao_az_com_o_caminho_que_ele_le()
    {
        var (saida, texto) = await RodarAsync("--dry-run", "--assinatura", Assinatura, "--grupo", "rg-devkit-app", "--grupo-clientes", "devkit-clients");
        var definicoes = texto.Split('\n', StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith("+ az role definition create", StringComparison.Ordinal)).ToArray();

        Assert.True(saida == 0, texto);
        Assert.Equal(2, definicoes.Length);
        Assert.All(definicoes, l => Assert.Matches(OperatingSystem.IsWindows() ? @"--role-definition @[A-Za-z]:\\" : "--role-definition @/", l));
    }

    [Fact]
    public async Task Sem_o_grupo_dos_clientes_o_script_recusa_antes_de_qualquer_az()
    {
        var (saida, texto) = await RodarAsync("--dry-run", "--assinatura", Assinatura, "--grupo", "rg-devkit-app");

        Assert.Equal(2, saida);
        Assert.Contains("--grupo-clientes", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("+ az", texto, StringComparison.Ordinal);
    }
}
