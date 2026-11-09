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

    [Theory]
    [InlineData("Design__Armazem", null, "Artefatos__ConexaoDoBlob | Design__ConexaoDoBlob")]
    [InlineData("Design__Armazem", "BlobEndpoint=https://ficticia.blob.core.windows.net;SharedAccessSignature=sv=ficticia", "Design__ConexaoDoBlob")]
    [InlineData("Artefatos__Armazem", "DefaultEndpointsProtocol=https;AccountName=ficticia;EndpointSuffix=core.windows.net", "Design__ConexaoDoBlob")]
    public async Task O_armazem_blob_dos_artefatos_sem_conexao_ou_sem_a_chave_da_conta_segura_o_deploy(string armazem, string? conexao, string falta)
    {
        var (saida, texto) = await ConferirAsync(Com(s =>
        {
            s[armazem] = "Blob";
            if (conexao is not null)
                s["Design__ConexaoDoBlob"] = conexao;
        }));

        Assert.Equal(1, saida);
        Assert.Equal([falta], Faltas(texto).Select(l => l[7..l.IndexOf(':')]));
        if (conexao is not null)
            Assert.DoesNotContain(conexao, texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conexao_propria_dos_artefatos_com_a_chave_da_conta_passa_sem_aviso_dos_artefatos()
    {
        const string conexao = "DefaultEndpointsProtocol=https;AccountName=ficticia;AccountKey=Y2hhdmUtZmljdGljaWE=;EndpointSuffix=core.windows.net";
        var (saida, texto) = await ConferirAsync(Com(s =>
        {
            s["Design__Armazem"] = "blob";
            s["Design__ConexaoDoBlob"] = "@Microsoft.KeyVault(SecretUri=https://cofre.vault.azure.net/secrets/design-blob/)";
            s["Artefatos__ConexaoDoBlob"] = conexao;
            s["Artefatos__TamanhoMaximoMb"] = "25";
        }));

        Assert.True(saida == 0, texto);
        Assert.Contains("ok     Artefatos__ConexaoDoBlob (a dos artefatos)", texto, StringComparison.Ordinal);
        Assert.Contains("ok     Artefatos__TamanhoMaximoMb", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("aviso  Artefatos__", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(conexao, texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_armazem_desconhecido_dos_artefatos_segura_o_deploy_e_o_disco_em_wwwroot_so_avisa()
    {
        var (desconhecido, textoDesconhecido) = await ConferirAsync(Com(s => s["Artefatos__Armazem"] = "azure"));
        var (padrao, textoPadrao) = await ConferirAsync(Completas);
        var (emHome, textoEmHome) = await ConferirAsync(Com(s =>
        {
            s["Artefatos__Armazem"] = "disco";
            s["Artefatos__Pasta"] = "/home/dados/artefatos";
        }));

        Assert.Equal(1, desconhecido);
        Assert.Equal(["Artefatos__Armazem"], Faltas(textoDesconhecido).Select(l => l[7..l.IndexOf(':')]));
        Assert.True(padrao == 0, textoPadrao);
        Assert.Contains("aviso  Design__Armazem: o zip do artefato vai para o disco da API, em wwwroot", textoPadrao, StringComparison.Ordinal);
        Assert.True(emHome == 0, textoEmHome);
        Assert.Contains("ok     Artefatos__Pasta (armazém em disco)", textoEmHome, StringComparison.Ordinal);
        Assert.DoesNotContain("aviso  Artefatos__", textoEmHome, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Artefatos__TamanhoMaximoMb", "cem", 1)]
    [InlineData("Artefatos__RetencaoDias", "7.5", 1)]
    [InlineData("Artefatos__LinkHoras", "99999999999", 1)]
    [InlineData("Artefatos__MaximoPorProjeto", "0", 0)]
    [InlineData("Artefatos__RetencaoDias", "-3", 0)]
    public async Task Os_numeros_dos_artefatos_que_nao_sao_inteiros_seguram_o_deploy_e_abaixo_de_1_so_avisam(string nome, string valor, int esperada)
    {
        var (saida, texto) = await ConferirAsync(Com(s => s[nome] = valor));

        Assert.Equal(esperada, saida);
        if (esperada == 1)
            Assert.Equal([nome], Faltas(texto).Select(l => l[7..l.IndexOf(':')]));
        else
            Assert.Contains($"aviso  {nome}: abaixo de 1: a API usa 1", texto, StringComparison.Ordinal);
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
        var noWorkflow = ScriptsDoJobSql(workflow);
        var secao = doc[doc.IndexOf("### Scripts SQL do controle remoto, na ordem", StringComparison.Ordinal)..];
        secao = secao[..secao.IndexOf("\n### ", 5, StringComparison.Ordinal)];
        var noDoc = Regex.Matches(secao, @"^\d+\. `(\S+\.sql)`", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(["Remoto.sql", "Provisao.sql", "Conversa.sql", "Decisoes.sql", "Push.sql", "CatalogoRemoto.sql", "ClienteDaMaquina.sql", "BoardRemoto.sql", "ArtefatosDosProjetos.sql"], noWorkflow);
        Assert.Equal(noWorkflow, noDoc);
        Assert.All(noWorkflow, nome => ScriptsDoRepositorio.Arquivo($"api/scripts/sqlserver/{nome}"));
        // O disparo manual diz quais scripts roda: todos, pelo nome.
        var descricao = Regex.Match(workflow, @"aplicar_sql:[\s\S]*?description: '([^']*)'").Groups[1].Value;
        Assert.All(noWorkflow, nome => Assert.Contains(Path.GetFileNameWithoutExtension(nome), descricao, StringComparison.Ordinal));
    }

    /// <summary>
    /// Todo script da pasta <c>api/scripts/sqlserver</c> vai no job <c>sql</c> (sugestão #32), salvo os
    /// <see cref="AnterioresAoControleRemoto"/>: o script novo esquecido fora do job reprova aqui, antes do deploy.
    /// </summary>
    [Fact]
    public async Task Todo_script_da_pasta_vai_no_job_sql_salvo_os_anteriores_ao_controle_remoto()
    {
        var workflow = await File.ReadAllTextAsync(ScriptsDoRepositorio.Arquivo(".github/workflows/main_devkit.yml"));
        var doc = await File.ReadAllTextAsync(ScriptsDoRepositorio.Arquivo("docs/publicacao-azure.md"));
        var pasta = Path.GetDirectoryName(ScriptsDoRepositorio.Arquivo("api/scripts/sqlserver/Remoto.sql"))!;
        var naPasta = Directory.GetFiles(pasta, "*.sql").Select(arquivo => Path.GetFileName(arquivo)).ToArray();
        var noJob = ScriptsDoJobSql(workflow);

        var fora = ForaDoJobSql(naPasta, noJob);

        Assert.True(fora.Length == 0,
            $"Script(s) de api/scripts/sqlserver fora do job sql do main_devkit.yml: {string.Join(", ", fora)}. Ponha cada um no job sql e na lista de docs/publicacao-azure.md (\"Scripts SQL do controle remoto, na ordem\").");
        // A lista congelada não apodrece: cada anterior existe na pasta, fica fora do job e tem a seção dele na publicação.
        Assert.All(AnterioresAoControleRemoto, nome =>
        {
            Assert.Contains(nome, naPasta);
            Assert.DoesNotContain(nome, noJob);
            Assert.Contains($"api/scripts/sqlserver/{nome}", doc, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void O_script_novo_fora_do_job_sql_reprova_a_conferencia()
    {
        string[] noJob = ["Remoto.sql", "Provisao.sql", "ArtefatosDosProjetos.sql"];

        Assert.Equal(["Novo.sql"], ForaDoJobSql(["Remoto.sql", "Us425.sql", "Novo.sql", "ArtefatosDosProjetos.sql", "Provisao.sql"], noJob));
        // O nome confere exato: um anterior com outra caixa é um script novo.
        Assert.Equal(["us425.sql"], ForaDoJobSql(["us425.sql", "Remoto.sql"], noJob));
        Assert.Empty(ForaDoJobSql([.. noJob, .. AnterioresAoControleRemoto], noJob));
    }

    [Theory]
    [InlineData("CatalogosRemotos", null)]
    [InlineData("ClientesDaMaquina", null)]
    [InlineData("AssercoesDoCliente", null)]
    [InlineData("PendenciasDispensadas", null)]
    [InlineData("ArtefatosDosProjetos", null)]
    [InlineData("ComandosRemotos", "Contexto")]
    [InlineData("FalasRemotas", "Anexos")]
    [InlineData("RespostasDasDecisoes", "Dispositivo")]
    public async Task As_tabelas_e_colunas_novas_do_controle_remoto_vem_de_um_script_aplicado_pelo_workflow(string tabela, string? coluna)
    {
        var workflow = await File.ReadAllTextAsync(ScriptsDoRepositorio.Arquivo(".github/workflows/main_devkit.yml"));
        var scripts = EsquemaDoBanco.ScriptsQueTrazem(tabela, coluna);

        var script = Assert.Single(scripts);
        Assert.Contains(script, ScriptsDoJobSql(workflow));
    }

    /// <summary>
    /// Os scripts de <c>api/scripts/sqlserver</c> ANTERIORES ao controle remoto: cada um tem a seção da sua US em
    /// <c>docs/publicacao-azure.md</c> e foi aplicado uma vez, à mão, ficando FORA do job <c>sql</c> — o
    /// <c>AcessoAosProjetos.sql</c> retira a tabela que o <c>DesignAcesso.sql</c> cria, e reaplicar a sequência a cada deploy
    /// não é seguro. A lista é CONGELADA: script novo vai no job.
    /// </summary>
    private static readonly string[] AnterioresAoControleRemoto =
    [
        "AcessoAosProjetos.sql", "AvaliacoesDeEntrega.sql", "DesignAcesso.sql", "Empresas.sql", "GruposDeErro.sql",
        "PedidosDeDemonstracao.sql", "RoiDeWorkItem.sql", "UltimoLogin.sql", "Us425.sql", "Us461.sql", "Us491.sql", "Us550.sql",
        "Us602.sql", "Us663.sql", "Us669.sql", "Us687.sql", "Us693.sql", "Us747.sql", "Usuarios.sql", "us738-origem-catalogo.sql",
    ];

    /// <summary>Os scripts da pasta que nem o job <c>sql</c> aplica nem são <see cref="AnterioresAoControleRemoto"/>, em ordem.</summary>
    private static string[] ForaDoJobSql(IEnumerable<string> naPasta, IReadOnlyCollection<string> noJob)
        => naPasta.Where(nome => !noJob.Contains(nome, StringComparer.Ordinal) && !AnterioresAoControleRemoto.Contains(nome, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();

    /// <summary>Os scripts do job <c>sql</c>, na ordem dos passos.</summary>
    private static string[] ScriptsDoJobSql(string workflow)
        => Regex.Matches(Job(workflow, "sql"), @"path: api/scripts/sqlserver/(\S+\.sql)").Select(m => m.Groups[1].Value).ToArray();

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
