using System.Text.RegularExpressions;
using DevKitPage.Infrastructure;

namespace DevKitPage.Provisao.Tests;

/// <summary>
/// A publicação do remote-control-v3 (item 7): a pré-checagem das app settings (<c>api/scripts/azure/conferir-app-settings.sh</c>)
/// segura o deploy quando falta o que a API ou o WebJob recusam na subida em Production, sem mostrar valor nenhum; o workflow
/// da <c>main</c> só aplica o SQL e publica com ela verde; e os scripts SQL do controle remoto vão na MESMA ordem no workflow e
/// em <c>docs/publicacao-azure.md</c>, com as tabelas novas do projeto dentro deles.
/// </summary>
public sealed class PublicacaoDoControleRemotoTests : IDisposable
{
    private const string Script = "api/scripts/azure/conferir-app-settings.sh";
    private const string SegredoFicticio = "segredo-ficticio-do-jwt-com-mais-de-32-caracteres";

    private readonly string _pasta = Directory.CreateTempSubdirectory("conferir-app-settings-").FullName;

    /// <summary>A configuração que a API e o WebJob aceitam em Production, no formato do az (nome, TAB, valor).</summary>
    private static readonly Dictionary<string, string> Completas = new(StringComparer.Ordinal)
    {
        ["Jwt__Segredo"] = SegredoFicticio,
        ["Banco__Provider"] = "SqlServer",
        ["ConnectionStrings__DevKitPage"] = "Server=tcp:ficticio.database.windows.net;Database=ficticia",
        ["Cors__Origens__0"] = "https://site.example",
        ["ProtecaoDeDados__Pasta"] = "/home/data/keyring",
        ["Provisao__AssinaturaId"] = "00000000-0000-0000-0000-000000000000",
        ["Provisao__Grupo"] = "devkit-clients",
    };

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private async Task<(int Saida, string Texto)> ConferirAsync(Dictionary<string, string> settings, params string[] conexoes)
    {
        var arquivo = Path.Combine(_pasta, "settings.tsv");
        await File.WriteAllLinesAsync(arquivo, settings.Select(s => $"{s.Key}\t{s.Value}"));
        var argumentos = new List<string> { "--settings", arquivo.Replace('\\', '/') };
        if (conexoes.Length > 0)
        {
            var lista = Path.Combine(_pasta, "conexoes.txt");
            await File.WriteAllLinesAsync(lista, conexoes);
            argumentos.AddRange(["--conexoes", lista.Replace('\\', '/')]);
        }
        return await ScriptsDoRepositorio.RodarAsync(Script, [.. argumentos]);
    }

    private static Dictionary<string, string> Com(Action<Dictionary<string, string>> mudar)
    {
        var settings = new Dictionary<string, string>(Completas, StringComparer.Ordinal);
        mudar(settings);
        return settings;
    }

    private static string[] Faltas(string texto)
        => texto.Split('\n', StringSplitOptions.TrimEntries).Where(l => l.StartsWith("FALTA", StringComparison.Ordinal)).ToArray();

    [Fact]
    public async Task A_configuracao_completa_deixa_o_deploy_seguir_sem_mostrar_valor_nenhum()
    {
        var (saida, texto) = await ConferirAsync(Completas);

        Assert.True(saida == 0, texto);
        Assert.Empty(Faltas(texto));
        Assert.Contains("o deploy segue", texto, StringComparison.Ordinal);
        Assert.All(Completas.Values, valor => Assert.DoesNotContain(valor, texto, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sem_o_key_ring_e_com_a_fila_simulada_o_deploy_nao_segue_e_diz_o_que_gravar()
    {
        var (saida, texto) = await ConferirAsync(Com(s =>
        {
            s.Remove("ProtecaoDeDados__Pasta");
            s["Remoto__Fila"] = "simulado";
        }));
        var faltas = Faltas(texto);

        Assert.Equal(1, saida);
        Assert.Equal(2, faltas.Length);
        Assert.Contains(faltas, l => l.StartsWith("FALTA  ProtecaoDeDados__Blob | ProtecaoDeDados__Pasta:", StringComparison.Ordinal));
        Assert.Contains(faltas, l => l.StartsWith("FALTA  Remoto__Fila:", StringComparison.Ordinal));
        Assert.Contains("o deploy NÃO segue", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(SegredoFicticio, texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_segredo_curto_o_provider_ausente_e_a_origem_com_barra_seguram_o_deploy()
    {
        var (saida, texto) = await ConferirAsync(Com(s =>
        {
            s["Jwt__Segredo"] = "curto-demais";
            s.Remove("Banco__Provider");
            s["Cors__Origens__0"] = "https://site.example/";
        }));
        var faltas = Faltas(texto);

        Assert.Equal(1, saida);
        Assert.Equal(["Jwt__Segredo", "Banco__Provider", "Cors__Origens__0"], faltas.Select(l => l[7..l.IndexOf(':')]));
        Assert.DoesNotContain("curto-demais", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_referencia_ao_Key_Vault_vale_como_segredo_e_a_connection_string_do_painel_vale_como_conexao()
    {
        var (saida, texto) = await ConferirAsync(Com(s =>
        {
            s["Jwt__Segredo"] = "@Microsoft.KeyVault(SecretUri=https://cofre.vault.azure.net/secrets/jwt-segredo/)";
            s.Remove("ConnectionStrings__DevKitPage");
        }), "DevKitPage");

        Assert.True(saida == 0, texto);
        Assert.Contains("ok     ConnectionStrings__DevKitPage", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_a_conexao_o_deploy_nao_segue()
    {
        var (saida, texto) = await ConferirAsync(Com(s => s.Remove("ConnectionStrings__DevKitPage")), "OutraBase");

        Assert.Equal(1, saida);
        Assert.Contains(Faltas(texto), l => l.StartsWith("FALTA  ConnectionStrings__DevKitPage:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task O_worker_sem_assinatura_e_grupo_ou_simulado_segura_o_deploy()
    {
        var (semGrupo, textoSemGrupo) = await ConferirAsync(Com(s =>
        {
            s.Remove("Provisao__AssinaturaId");
            s.Remove("Provisao__Grupo");
        }));
        var (simulado, textoSimulado) = await ConferirAsync(Com(s => s["Provisao__Provedor"] = "Simulado"));

        Assert.Equal(1, semGrupo);
        Assert.Equal(["Provisao__AssinaturaId", "Provisao__Grupo"], Faltas(textoSemGrupo).Select(l => l[7..l.IndexOf(':')]));
        Assert.Equal(1, simulado);
        Assert.Contains(Faltas(textoSimulado), l => l.StartsWith("FALTA  Provisao__Provedor:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ClienteDaMaquina__ValidadeDoTokenMinutos", "61")]
    [InlineData("ClienteDaMaquina__ValidadeMaximaDaAssercaoSegundos", "29")]
    [InlineData("ClienteDaMaquina__ToleranciaDoRelogioSegundos", "-1")]
    [InlineData("ClienteDaMaquina__ToleranciaDoRelogioSegundos", "sessenta")]
    [InlineData("ClienteDaMaquina__Habilitado", "sim")]
    public async Task O_client_credentials_da_maquina_fora_da_faixa_segura_o_deploy(string nome, string valor)
    {
        var (saida, texto) = await ConferirAsync(Com(s => s[nome] = valor));

        Assert.Equal(1, saida);
        Assert.Equal([nome], Faltas(texto).Select(l => l[7..l.IndexOf(':')]));
    }

    [Fact]
    public async Task O_client_credentials_da_maquina_dentro_da_faixa_passa()
    {
        var (saida, texto) = await ConferirAsync(Com(s =>
        {
            s["ClienteDaMaquina__Habilitado"] = "False";
            s["ClienteDaMaquina__ValidadeDoTokenMinutos"] = "60";
            s["ClienteDaMaquina__ValidadeMaximaDaAssercaoSegundos"] = "30";
            s["ClienteDaMaquina__ToleranciaDoRelogioSegundos"] = "0";
        }));

        Assert.True(saida == 0, texto);
    }

    [Fact]
    public async Task O_que_so_degrada_um_recurso_e_aviso_e_nao_segura_o_deploy()
    {
        var (saida, texto) = await ConferirAsync(Completas);

        Assert.True(saida == 0, texto);
        foreach (var nome in new[] { "Licencas__ChavePrivada", "Push__ChavePublica", "REPO_KEY", "Provisao__IdentidadeClientId" })
            Assert.Contains($"aviso  {nome}:", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_workflow_da_main_so_aplica_o_sql_e_publica_com_a_configuracao_conferida()
    {
        var workflow = await File.ReadAllTextAsync(ScriptsDoRepositorio.Arquivo(".github/workflows/main_devkit.yml"));
        var configuracao = Job(workflow, "configuracao");
        var sql = Job(workflow, "sql");
        var deploy = Job(workflow, "deploy");

        Assert.Contains($"run: bash {Script} --app devkit", configuracao, StringComparison.Ordinal);
        Assert.Contains("uses: azure/login@v2", configuracao, StringComparison.Ordinal);
        Assert.Contains("needs: configuracao", sql, StringComparison.Ordinal);
        Assert.Matches(@"needs: \[[^\]]*\bconfiguracao\b[^\]]*\]", deploy);
        Assert.Contains("needs.configuracao.result == 'success'", deploy, StringComparison.Ordinal);
        Assert.Contains("app-name: 'devkit'", deploy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Os_scripts_do_controle_remoto_vao_na_mesma_ordem_no_workflow_e_na_publicacao()
    {
        var workflow = await File.ReadAllTextAsync(ScriptsDoRepositorio.Arquivo(".github/workflows/main_devkit.yml"));
        var doc = await File.ReadAllTextAsync(ScriptsDoRepositorio.Arquivo("docs/publicacao-azure.md"));
        var noWorkflow = Regex.Matches(Job(workflow, "sql"), @"path: api/scripts/sqlserver/(\S+\.sql)").Select(m => m.Groups[1].Value).ToArray();
        var secao = doc[doc.IndexOf("### Scripts SQL do controle remoto, na ordem", StringComparison.Ordinal)..];
        secao = secao[..secao.IndexOf("\n### ", 5, StringComparison.Ordinal)];
        var noDoc = Regex.Matches(secao, @"^\d+\. `(\S+\.sql)`", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(["Remoto.sql", "Provisao.sql", "Conversa.sql", "Decisoes.sql", "Push.sql", "CatalogoRemoto.sql", "ClienteDaMaquina.sql"], noWorkflow);
        Assert.Equal(noWorkflow, noDoc);
        Assert.All(noWorkflow, nome => ScriptsDoRepositorio.Arquivo($"api/scripts/sqlserver/{nome}"));
        // O disparo manual diz quais scripts roda: todos, pelo nome.
        var descricao = Regex.Match(workflow, @"aplicar_sql:[\s\S]*?description: '([^']*)'").Groups[1].Value;
        Assert.All(noWorkflow, nome => Assert.Contains(Path.GetFileNameWithoutExtension(nome), descricao, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("CatalogosRemotos")]
    [InlineData("ClientesDaMaquina")]
    [InlineData("AssercoesDoCliente")]
    public async Task As_tabelas_novas_do_remote_control_v3_vem_de_um_script_aplicado_pelo_workflow(string tabela)
    {
        var workflow = await File.ReadAllTextAsync(ScriptsDoRepositorio.Arquivo(".github/workflows/main_devkit.yml"));
        var scripts = EsquemaDoBanco.ScriptsQueTrazem(tabela, null);

        var script = Assert.Single(scripts);
        Assert.Contains($"path: api/scripts/sqlserver/{script}", Job(workflow, "sql"), StringComparison.Ordinal);
    }

    /// <summary>O bloco de um job do workflow: da linha <c>  nome:</c> até o próximo job (dois espaços de recuo).</summary>
    private static string Job(string workflow, string nome)
    {
        var linhas = workflow.Replace("\r\n", "\n").Split('\n');
        var inicio = Array.FindIndex(linhas, l => l == $"  {nome}:");
        Assert.True(inicio >= 0, $"O job {nome} não está no workflow.");
        var fim = Array.FindIndex(linhas, inicio + 1, l => Regex.IsMatch(l, @"^  [A-Za-z_-]+:\s*$"));
        return string.Join('\n', linhas[inicio..(fim < 0 ? linhas.Length : fim)]);
    }
}
