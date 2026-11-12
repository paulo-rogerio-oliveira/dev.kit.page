using System.Diagnostics;
using System.Text;

namespace DevKitPage.Provisao.Tests;

/// <summary>
/// Os arquivos versionados da publicação (os scripts de <c>api/scripts</c>, os workflows e os docs), achados subindo da pasta
/// dos testes até a raiz do repositório, e o bash que roda os scripts.
/// </summary>
internal static class ScriptsDoRepositorio
{
    /// <summary>A raiz do repositório: a primeira pasta acima dos testes com o <c>.github</c>.</summary>
    public static string Raiz()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            if (Directory.Exists(Path.Combine(pasta.FullName, ".github")) && Directory.Exists(Path.Combine(pasta.FullName, "api")))
                return pasta.FullName;
        }
        throw new DirectoryNotFoundException("A raiz do repositório (com .github e api) não foi achada acima da pasta dos testes.");
    }

    /// <summary>O caminho de um fonte local ou documento centralizado, que tem de existir.</summary>
    public static string Arquivo(string relativo)
    {
        var raiz = Raiz();
        if (relativo.StartsWith("docs/", StringComparison.Ordinal))
        {
            // O CI informa a pasta do produto no checkout fixo de dev.kit.docs; localmente, os clones ficam lado a lado.
            raiz = Environment.GetEnvironmentVariable("DEVKIT_PAGE_DOCS_RAIZ")
                ?? Path.Combine(Directory.GetParent(raiz)!.FullName, "dev.kit.docs", "dev.kit.page");
        }
        var caminho = Path.Combine(raiz, relativo.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(caminho))
            throw new FileNotFoundException($"{relativo} não foi achado na origem esperada.", caminho);
        return caminho;
    }

    /// <summary>O bash: o do Git no Windows (o do System32 é o do WSL), o do PATH no CI.</summary>
    public static string Bash()
    {
        if (!OperatingSystem.IsWindows())
            return "bash";
        var git = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        Assert.True(File.Exists(git), $"O teste do script precisa do Git Bash em {git}.");
        return git;
    }

    /// <summary>Roda o script do repositório pelo bash e devolve a saída e o texto (a saída padrão seguida da de erro).</summary>
    public static async Task<(int Saida, string Texto)> RodarAsync(string script, params string[] argumentos)
    {
        // O bash escreve em UTF-8; sem isto, no Windows a saída seria lida na página de código do console (os acentos quebram).
        var inicio = new ProcessStartInfo(Bash())
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        inicio.ArgumentList.Add(Arquivo(script).Replace('\\', '/'));
        foreach (var argumento in argumentos)
            inicio.ArgumentList.Add(argumento);
        // O teste não roda dentro do Actions: as anotações ::error:: do script ficam de fora.
        inicio.Environment.Remove("GITHUB_ACTIONS");
        using var processo = Process.Start(inicio)!;
        var saida = processo.StandardOutput.ReadToEndAsync();
        var erro = processo.StandardError.ReadToEndAsync();
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await processo.WaitForExitAsync(limite.Token);
        return (processo.ExitCode, await saida + await erro);
    }
}
