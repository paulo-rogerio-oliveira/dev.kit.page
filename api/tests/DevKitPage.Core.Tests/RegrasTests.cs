using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>As regras puras: a validação do lote, a qualidade com divisor zero, a chave da máquina, a senha e o período.</summary>
public sealed class RegrasTests
{
    private static readonly DateTimeOffset Em = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static TelemetryBatchV1 Lote(int eventos, string versao = "v1", string maquina = "m1")
        => new(versao, maquina, "1.4.0", Enumerable.Range(1, eventos)
            .Select(i => new TelemetryEventV1($"e{i}", TiposDeEvento.TurnoExecutado, "s1", 1, 1000, "claude", Em)).ToArray());

    [Fact]
    public void Lote_valido_passa()
        => Assert.Null(ValidadorDeLote.Validar(Lote(3), "m1", 10));

    [Fact]
    public void Lote_acima_do_maximo_e_413()
        => Assert.Equal(413, ValidadorDeLote.Validar(Lote(11), "m1", 10)!.Status);

    [Theory]
    [InlineData("v2", "m1")]
    [InlineData("v1", "outra")]
    public void Versao_desconhecida_ou_outra_maquina_e_400(string versao, string maquina)
        => Assert.Equal(400, ValidadorDeLote.Validar(Lote(1, versao, maquina), "m1", 10)!.Status);

    [Fact]
    public void Evento_sem_id_ou_com_quantidade_negativa_e_400()
    {
        var semId = new TelemetryBatchV1("v1", "m1", "x", new[] { new TelemetryEventV1(" ", "TurnoExecutado", "s", 1, null, "", Em) });
        var negativo = new TelemetryBatchV1("v1", "m1", "x", new[] { new TelemetryEventV1("e1", "TurnoExecutado", "s", -1, null, "", Em) });

        Assert.Equal(400, ValidadorDeLote.Validar(semId, "m1", 10)!.Status);
        Assert.Equal(400, ValidadorDeLote.Validar(negativo, "m1", 10)!.Status);
        Assert.Equal(400, ValidadorDeLote.Validar(null, "m1", 10)!.Status);
    }

    [Fact]
    public void Qualidade_sem_denominador_e_nula_e_nao_nan()
    {
        var periodo = new Periodo(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3));

        var vazia = CalculoDeQualidade.Calcular(periodo, Array.Empty<SomaPorTipo>());

        Assert.Null(vazia.TaxaDeFalha);
        Assert.Null(vazia.NotaMedia);
        Assert.Null(vazia.RazaoCumpridosRecusados);
        Assert.Null(vazia.TurnosPorObjetivoCumprido);
        Assert.Null(vazia.DuracaoMediaDoTurnoMs);
        Assert.Empty(vazia.FalhasPorCausa);
    }

    [Fact]
    public void Qualidade_soma_as_razoes_e_as_falhas_por_causa()
    {
        var somas = new[]
        {
            new SomaPorTipo(TiposDeEvento.TurnoExecutado, "claude", 3, 3, 30000),
            new SomaPorTipo(TiposDeEvento.TurnoExecutado, "kiro", 1, 1, 10000),
            new SomaPorTipo(TiposDeEvento.TurnoFalhou, "limite-de-uso", 1, 1, 0),
            new SomaPorTipo(TiposDeEvento.ObjetivoAvaliado, "", 2, 2, 180),
            new SomaPorTipo(TiposDeEvento.ObjetivoCumprido, "", 2, 2, 0),
            new SomaPorTipo(TiposDeEvento.ObjetivoRecusado, "", 1, 1, 0),
        };

        var q = CalculoDeQualidade.Calcular(new Periodo(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)), somas);

        Assert.Equal(4, q.Turnos);
        Assert.Equal(0.25, q.TaxaDeFalha);
        Assert.Equal(10000, q.DuracaoMediaDoTurnoMs);
        Assert.Equal(90, q.NotaMedia);
        Assert.Equal(2.0, q.RazaoCumpridosRecusados);
        Assert.Equal(2.0, q.TurnosPorObjetivoCumprido);
        Assert.Equal(new CausaDeFalha("limite-de-uso", 1), Assert.Single(q.FalhasPorCausa));
    }

    [Fact]
    public void Chave_da_maquina_e_aleatoria_e_so_o_hash_confere()
    {
        var (chave, hash) = ChaveDeMaquina.Gerar();
        var (outra, _) = ChaveDeMaquina.Gerar();

        Assert.NotEqual(chave, outra);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(chave, hash);
        Assert.Equal(hash, ChaveDeMaquina.Hash(chave));
        Assert.True(ChaveDeMaquina.Iguais("abc", "abc"));
        Assert.False(ChaveDeMaquina.Iguais("abc", "abd"));
    }

    [Theory]
    [InlineData("curta1", false)]
    [InlineData("somenteletras", false)]
    [InlineData("1234567890", false)]
    [InlineData("senhaBoa2026", true)]
    public void Politica_de_senha(string senha, bool valida)
        => Assert.Equal(valida, PoliticaDeSenha.Validar(senha).Length == 0);

    [Fact]
    public void Lote_v1_sem_os_campos_novos_continua_valido_e_com_eles_tambem()
    {
        var antigo = new TelemetryBatchV1("v1", "m1", "1.4.0", new[] { new TelemetryEventV1("e1", TiposDeEvento.TurnoFalhou, "s", 1, null, "nao-classificada", Em) });
        var novo = new TelemetryBatchV1("v1", "m1", "1.5.0", new[]
        {
            new TelemetryEventV1("e2", TiposDeEvento.ExcecaoNaoClassificada, "s", 1, null, "abc", Em, "System.Exception: x", "abc"),
        });

        Assert.Null(ValidadorDeLote.Validar(antigo, "m1", 10));
        Assert.Null(ValidadorDeLote.Validar(novo, "m1", 10));
        Assert.Contains(TiposDeEvento.ExcecaoNaoClassificada, TiposDeEvento.Conhecidos);
    }

    [Fact]
    public void Trace_e_cortado_em_8_kb_e_o_recorte_continua_em_200()
    {
        var trace = RegrasDeErro.Mascarar("System.Exception: x\n" + new string('a', 20_000));

        Assert.Equal(RegrasDeErro.TamanhoMaximoDoTrace, trace.Length);
        Assert.Equal(8192, RegrasDeErro.TamanhoMaximoDoTrace);
        Assert.Equal(200, ValidadorDeLote.TamanhoMaximoDoTexto);
    }

    [Theory]
    [InlineData(@"at X.Y() in C:\Users\ana\src\Y.cs:line 9", "ana")]
    [InlineData(@"at X.Y() in C:\Program Files\dev kit\Y.cs:line 9", "Program Files")]
    [InlineData(@"falhou em \\servidor\share\pasta\a.txt", "servidor")]
    [InlineData("ENOENT /home/ana/projeto/a.json", "ana")]
    [InlineData("mande para ana.souza@cliente.com.br", "cliente")]
    [InlineData("GET https://cliente.visualstudio.com/_apis?token=abc falhou", "token")]
    [InlineData("sessão 3f2b8c1e-9d4a-4c6e-8f00-123456789abc", "3f2b8c1e")]
    [InlineData("fatal: '/c/Users/ana/repos/cliente' is not a git repository", "ana")]
    [InlineData("git@ssh.dev.azure.com:v3/acme/erp/erp: Permission denied (publickey)", "acme")]
    [InlineData("fatal: unable to access 'ssh://git@github.com/acme/erp.git'", "acme")]
    [InlineData(@"Acesso negado para ACME\ana.souza ao abrir o serviço", "ana.souza")]
    public void Mascara_de_defesa_tira_caminho_email_url_e_guid(string texto, string vazado)
    {
        var mascarado = RegrasDeErro.Mascarar(texto);

        Assert.DoesNotContain(vazado, mascarado);
        Assert.Matches("<(caminho|email|url|guid|usuario)>", mascarado);
    }

    [Fact]
    public void Mascara_mantem_os_quadros_e_a_linha()
        => Assert.Equal("at GitKit.Core.A.B() in <caminho>:line 42", RegrasDeErro.Mascarar(@"at GitKit.Core.A.B() in C:\gtk\5\src\A.cs:line 42"));

    [Fact]
    public void Tipo_e_a_primeira_linha_ate_os_dois_pontos_e_a_assinatura_nunca_fica_vazia()
    {
        Assert.Equal("System.IO.IOException", RegrasDeErro.Tipo("System.IO.IOException: disco cheio\n   at A.B()"));
        Assert.Equal("abc", RegrasDeErro.Assinatura(" abc ", "x"));
        Assert.StartsWith("t", RegrasDeErro.Assinatura(null, "System.Exception: x"));
        Assert.Equal(RegrasDeErro.Assinatura(null, "System.Exception: x"), RegrasDeErro.Assinatura("", "System.Exception: x"));
    }

    [Theory]
    [InlineData(EstadosDoGrupo.Novo, null, "2.0.0", EstadosDoGrupo.Novo)]
    [InlineData(EstadosDoGrupo.Visto, null, "2.0.0", EstadosDoGrupo.Visto)]
    [InlineData(EstadosDoGrupo.Ignorado, null, "2.0.0", EstadosDoGrupo.Ignorado)]
    [InlineData(EstadosDoGrupo.Resolvido, "1.5.0", "1.4.9", EstadosDoGrupo.Resolvido)]
    [InlineData(EstadosDoGrupo.Resolvido, "1.5.0", "1.5.0", EstadosDoGrupo.Regrediu)]
    [InlineData(EstadosDoGrupo.Resolvido, "1.5.0", "1.10.0", EstadosDoGrupo.Regrediu)]
    [InlineData(EstadosDoGrupo.Resolvido, "v1.5.0", "1.5.1-beta", EstadosDoGrupo.Regrediu)]
    [InlineData(EstadosDoGrupo.Resolvido, null, "1.0.0", EstadosDoGrupo.Regrediu)]
    public void Ocorrencia_nova_so_muda_o_grupo_resolvido_que_volta_na_versao_da_correcao(string estado, string? resolvidoNa, string versao, string esperado)
        => Assert.Equal(esperado, RegrasDeErro.EstadoAoReceber(estado, resolvidoNa, versao));

    [Theory]
    [InlineData(EstadosDoGrupo.Visto, null, true)]
    [InlineData(EstadosDoGrupo.Resolvido, "1.5.0", true)]
    [InlineData(EstadosDoGrupo.Ignorado, null, true)]
    [InlineData(EstadosDoGrupo.Novo, null, true)]
    [InlineData(EstadosDoGrupo.Regrediu, null, false)]
    [InlineData("Apagado", null, false)]
    public void So_os_estados_escolhiveis_sao_aceitos_na_reacao(string estado, string? versao, bool aceito)
        => Assert.Equal(aceito, RegrasDeErro.Validar(new AlterarEstadoDoGrupo(estado, versao)).Length == 0);

    [Theory]
    [InlineData("admin", null, true, null)]
    [InlineData("admin", "7", true, null)] // o admin é tudo, mesmo com uma empresa na claim
    [InlineData("gestor", "7", false, 7)]
    public void Escopo_sai_do_papel_e_da_empresa(string papel, string? empresa, bool ehAdmin, int? empresaId)
    {
        var escopo = EscopoDoPainel.DasClaims(papel, empresa)!;

        Assert.Equal((ehAdmin, empresaId), (escopo.EhAdmin, escopo.EmpresaId));
    }

    [Theory]
    [InlineData("gestor", null)]
    [InlineData("gestor", "")]
    [InlineData("gestor", "abc")]
    [InlineData("gestor", "-1")]
    [InlineData("gestor", "0")]
    [InlineData(null, null)]
    [InlineData("visitante", "7")]
    public void Token_sem_escopo_valido_nunca_vira_ver_tudo(string? papel, string? empresa)
        => Assert.Null(EscopoDoPainel.DasClaims(papel, empresa));

    [Fact]
    public void Empresa_valida_nome_plano_e_assentos_e_o_login_do_gestor()
    {
        Assert.Empty(ValidadorDeEmpresa.Validar(new EmpresaNova("Empresa A", "Empresarial", 10)));
        Assert.Equal(new[] { "assentos", "nome", "plano" }, ValidadorDeEmpresa.Validar(new EmpresaNova(" ", "", 0)).Keys.Order());
        Assert.Empty(ValidadorDeEmpresa.ValidarLogin("gestor.a"));
        Assert.NotEmpty(ValidadorDeEmpresa.ValidarLogin("gestor a"));
        Assert.Equal(100, ValidadorDeEmpresa.Colaborador(new string('x', 300)).Length);
    }

    [Fact]
    public void Codigo_de_adesao_e_aleatorio_e_compara_sem_caixa_nem_espaco()
    {
        var codigo = CodigoDeAdesao.Gerar();

        Assert.Matches("^DK-[A-Z2-9]{4}-[A-Z2-9]{4}$", codigo);
        Assert.NotEqual(codigo, CodigoDeAdesao.Gerar());
        Assert.Equal(codigo, CodigoDeAdesao.Normalizar($"  {codigo.ToLowerInvariant()} "));
    }

    [Theory]
    [InlineData("Ana", "Ana")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("Silva; Ana", "\"Silva; Ana\"")]
    [InlineData("O \"Ana\"", "\"O \"\"Ana\"\"\"")]
    public void Celula_do_csv_neutraliza_formula_e_escapa_o_separador(string valor, string esperado)
        => Assert.Equal(esperado, ExportacaoCsv.Celula(valor));

    [Fact]
    public void Csv_tem_o_cabecalho_e_uma_linha_por_colaborador_e_dia()
    {
        var csv = ExportacaoCsv.Gerar([new LinhaExportada("Ana", "máquina a1", "Empresa A", new DateOnly(2026, 10, 3), 1, 2, 0, 5, 1, 3, 1, 0)]);

        var linhas = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(string.Join(';', ExportacaoCsv.Cabecalho), linhas[0]);
        Assert.Equal("Ana;máquina a1;Empresa A;2026-10-03;1;2;0;5;1;3;1;0", linhas[1]);
    }

    [Fact]
    public void Periodo_padrao_sao_os_ultimos_30_dias_e_a_ordem_e_garantida()
    {
        var hoje = new DateOnly(2026, 10, 3);

        Assert.Equal(new Periodo(new DateOnly(2026, 9, 4), hoje), Periodo.Pedido(null, null, hoje));
        Assert.Equal(new Periodo(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5)), Periodo.Pedido(new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 1), hoje));
    }
}
