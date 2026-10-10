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

    // US #399: os tipos novos entram nos totais desde já — fora de Conhecidos, a API os contaria como
    // "ignorados" e o histórico se perderia até o painel existir.
    [Theory]
    [InlineData(TiposDeEvento.EntregaAvaliada)]
    [InlineData(TiposDeEvento.EntregaPronta)]
    [InlineData(TiposDeEvento.RevisaoHumana)]
    [InlineData(TiposDeEvento.TokensConsumidos)]
    [InlineData(TiposDeEvento.CustoEstimado)]
    [InlineData(TiposDeEvento.AgenteTrocado)]
    [InlineData(TiposDeEvento.ComandoNegado)]
    public void Os_tipos_da_us_399_sao_gravados(string tipo)
    {
        Assert.Contains(tipo, TiposDeEvento.Conhecidos);
        var lote = new TelemetryBatchV1("v1", "m1", "1.6.0", new[] { new TelemetryEventV1("e399", tipo, "s", 1, 1, "detalhe", Em) });
        Assert.Null(ValidadorDeLote.Validar(lote, "m1", 10));
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
    [InlineData("dev", null)] // US #405: o dev entra no app, mas não no painel
    [InlineData("dev", "7")]
    public void Token_sem_escopo_valido_nunca_vira_ver_tudo(string? papel, string? empresa)
        => Assert.Null(EscopoDoPainel.DasClaims(papel, empresa));

    [Theory]
    [InlineData(Papeis.Admin, true)]
    [InlineData(Papeis.Gestor, true)]
    [InlineData(Papeis.Dev, false)]
    [InlineData(null, false)]
    [InlineData("visitante", false)]
    public void So_admin_e_gestor_entram_no_painel(string? papel, bool entra)
        => Assert.Equal(entra, Papeis.EntraNoPainel(papel));

    // ----- US #405: o impasse e o árbitro (#411) -----

    [Theory]
    [InlineData(TiposDeEvento.ImpasseDetectado)]
    [InlineData(TiposDeEvento.ImpasseResolvido)]
    [InlineData(TiposDeEvento.ArbitroAgiu)]
    public void Os_tipos_do_impasse_e_do_arbitro_sao_gravados(string tipo)
    {
        Assert.Contains(tipo, TiposDeEvento.Conhecidos);
        var lote = new TelemetryBatchV1("v1", "m1", "1.7.0", new[] { new TelemetryEventV1("e405", tipo, "s", 1, 3, "cobrou|Arquivos alterados no turno", Em) });
        Assert.Null(ValidadorDeLote.Validar(lote, "m1", 10));
    }

    [Fact]
    public void Impasses_e_arbitro_agregam_quantidade_tempo_desfecho_regra_e_taxa_de_correcao()
    {
        var somas = new[]
        {
            new SomaPorTipo(TiposDeEvento.ImpasseDetectado, OrigensDoImpasse.MensagemParada, 2, 2, 60),
            new SomaPorTipo(TiposDeEvento.ImpasseDetectado, OrigensDoImpasse.ObjetivoParado, 1, 1, 30),
            new SomaPorTipo(TiposDeEvento.ImpasseResolvido, "arbitro-reagiu", 2, 2, 20),
            new SomaPorTipo(TiposDeEvento.ImpasseResolvido, "dev-falou", 1, 1, 40),
            new SomaPorTipo(TiposDeEvento.ArbitroAgiu, "cobrou|Arquivos alterados no turno", 3, 3, 6),
            new SomaPorTipo(TiposDeEvento.ArbitroAgiu, "cobrou|Idioma e fluxo", 1, 1, 1),
            new SomaPorTipo(TiposDeEvento.ArbitroAgiu, "cobrou", 0, 0, 0),
            new SomaPorTipo(TiposDeEvento.ArbitroAgiu, "corrigido|Arquivos alterados no turno", 3, 3, 9),
            new SomaPorTipo(TiposDeEvento.ArbitroAgiu, "escalou-ao-dev", 1, 1, 4),
        };

        var q = CalculoDeQualidade.Calcular(new Periodo(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)), somas);

        var impasses = q.Impasses!;
        Assert.Equal((3L, 30.0, 3L), (impasses.Detectados, impasses.MinutosParadoNaDeteccao, impasses.Destravados));
        Assert.Equal(20.0, impasses.MinutosAteDestravar);
        Assert.Equal(new[] { new ContagemPorRecorte(OrigensDoImpasse.MensagemParada, 2), new ContagemPorRecorte(OrigensDoImpasse.ObjetivoParado, 1) }, impasses.PorOrigem);
        Assert.Equal(new[] { new ContagemPorRecorte("arbitro-reagiu", 2), new ContagemPorRecorte("dev-falou", 1) }, impasses.ComoDestravaram);

        var arbitro = q.Arbitro!;
        Assert.Equal((8L, 4L, 3L, 0.75, 1L), (arbitro.Acoes, arbitro.Cobrancas, arbitro.Corrigidas, arbitro.TaxaDeCorrecao, arbitro.EscaladasAoDev));
        Assert.Equal(new[] { new ContagemPorRecorte("Arquivos alterados no turno", 3), new ContagemPorRecorte("Idioma e fluxo", 1) },
            arbitro.CobrancasPorRegra.Where(c => c.Quantidade > 0));
        Assert.Equal("cobrou", arbitro.PorAcao[0].Recorte);
    }

    [Fact]
    public void Impasses_e_arbitro_sem_denominador_sao_nulos_e_nao_nan()
    {
        var vazia = CalculoDeQualidade.Calcular(new Periodo(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)), Array.Empty<SomaPorTipo>());
        // Corrigidas sem nenhuma cobrança no período: a taxa continua sem denominador.
        var soCorrigido = CalculoDeQualidade.Calcular(new Periodo(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9)),
            [new SomaPorTipo(TiposDeEvento.ArbitroAgiu, "corrigido", 2, 2, 0)]);

        Assert.Equal(0, vazia.Impasses!.Detectados);
        Assert.Null(vazia.Impasses.MinutosParadoNaDeteccao);
        Assert.Null(vazia.Impasses.MinutosAteDestravar);
        Assert.Empty(vazia.Impasses.ComoDestravaram);
        Assert.Null(vazia.Arbitro!.TaxaDeCorrecao);
        Assert.Empty(vazia.Arbitro.CobrancasPorRegra);
        Assert.Null(soCorrigido.Arbitro!.TaxaDeCorrecao);
        Assert.Equal(2, soCorrigido.Arbitro.Corrigidas);
    }

    [Theory]
    [InlineData("cobrou|Arquivos alterados no turno", "cobrou", "Arquivos alterados no turno")]
    [InlineData("escalou-ao-dev", "escalou-ao-dev", "")]
    [InlineData("cobrou|Regra|com barra", "cobrou", "Regra|com barra")] // só o PRIMEIRO | separa
    [InlineData("", "", "")]
    public void Recorte_do_arbitro_separa_a_acao_da_secao_pelo_primeiro_separador(string detalhe, string acao, string secao)
        => Assert.Equal((acao, secao), RegrasDoArbitro.Separar(detalhe));

    // ----- US #405: a versão do dev.kit pelas GitHub Releases (#406) -----

    [Theory]
    [InlineData("v136", 136)]
    [InlineData("V7", 7)]
    [InlineData("v1.2.3", 1)] // a legada vX.Y.Z vale X
    [InlineData("v12.0", 12)]
    [InlineData(" v140 ", 140)]
    [InlineData("v1.2.3-beta", null)] // sufixo de prerelease: ilegível
    [InlineData("v0", null)]
    [InlineData("136", null)] // sem o v não é tag do dev.kit
    [InlineData("latest", null)]
    [InlineData("v", null)]
    [InlineData("vabc", null)]
    [InlineData("v99999999999", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Tag_vira_o_numero_do_devkit_ou_nada(string? tag, int? versao)
        => Assert.Equal(versao, RegrasDeVersao.Versao(tag));

    [Fact]
    public void Estaveis_ignora_prerelease_rascunho_e_tag_ilegivel_e_ordena_pelo_numero()
    {
        var dia = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var releases = new[]
        {
            new RegrasDeVersao.Candidata("v99", false, false, dia),
            new RegrasDeVersao.Candidata("v137", true, false, dia.AddDays(3)),  // prerelease
            new RegrasDeVersao.Candidata("v138", false, true, null),            // rascunho
            new RegrasDeVersao.Candidata("nightly", false, false, dia.AddDays(4)), // ilegível
            new RegrasDeVersao.Candidata("v136", false, false, dia.AddDays(2)),
            new RegrasDeVersao.Candidata("v1.2.3", false, false, dia.AddDays(-30)),
            new RegrasDeVersao.Candidata("v1.5.0", false, false, dia.AddDays(-20)), // a mesma versão 1: fica a mais recente
        };

        var estaveis = RegrasDeVersao.Estaveis(releases, r => r);

        Assert.Equal(new[] { 136, 99, 1 }, estaveis.Select(e => e.Versao)); // numérica: 136 antes de 99
        Assert.Equal("v1.5.0", estaveis[^1].Release.Tag);
        Assert.Empty(RegrasDeVersao.Estaveis(new[] { new RegrasDeVersao.Candidata("v2", true, false, dia) }, r => r));
    }

    [Fact]
    public void Destaques_sao_os_itens_de_lista_sem_markdown_no_maximo_cinco()
    {
        const string corpo = "## Novidades\r\n\r\nTexto de abertura.\r\n- **\"Precisa de você\"** no Board\r\n* Aviso de [nova versão](https://x/y) no app\r\n"
            + "+ `devcli versao` mostra a versão\r\n- [x] Configurador obrigatório\r\n-sem espaço não é item\r\n- ![imagem](a.png)\r\n- quinto\r\n- sexto\r\n- sétimo";

        var destaques = RegrasDeVersao.Destaques(corpo);

        Assert.Equal(new[] { "\"Precisa de você\" no Board", "Aviso de nova versão no app", "devcli versao mostra a versão", "Configurador obrigatório", "quinto" }, destaques);
        Assert.Empty(RegrasDeVersao.Destaques(null));
        Assert.EndsWith("…", RegrasDeVersao.SemMarkdown(new string('a', 300)));
    }

    [Theory]
    [InlineData("sha256:ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    [InlineData("sha1:abc", null)]
    [InlineData(null, null)]
    public void Sha256_do_digest_do_asset(string? digest, string? esperado)
        => Assert.Equal(esperado, RegrasDeVersao.Sha256DoDigest(digest));

    [Theory]
    [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789  devkit-136.zip\n")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void Sha256_do_arquivo_aceita_o_formato_do_sha256sum_e_o_hash_puro(string conteudo)
        => Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", RegrasDeVersao.Sha256DoArquivo(conteudo));

    [Fact]
    public void Sha256_do_arquivo_sem_hash_e_nulo()
        => Assert.Null(RegrasDeVersao.Sha256DoArquivo("não é um hash"));

    // ----- US #405: a gestão de usuários (#407) -----

    [Fact]
    public void Senha_temporaria_e_aleatoria_e_passa_na_politica()
    {
        var senha = GeradorDeSenha.Temporaria();

        Assert.Empty(PoliticaDeSenha.Validar(senha));
        Assert.NotEqual(senha, GeradorDeSenha.Temporaria());
    }

    [Theory]
    [InlineData("ana.dev", "Ana", Papeis.Dev, null, new string[0])]
    [InlineData("ana.dev", "Ana", Papeis.Dev, 3, new string[0])]
    [InlineData("gestor.a", "", Papeis.Gestor, 3, new string[0])]
    [InlineData("gestor.a", "", Papeis.Gestor, null, new[] { "empresaId" })]
    [InlineData("root", "", Papeis.Admin, 3, new[] { "empresaId" })]
    [InlineData("ana dev", "", "visitante", null, new[] { "login", "papel" })]
    public void Usuario_novo_valida_login_papel_e_empresa(string login, string nome, string papel, int? empresa, string[] campos)
        => Assert.Equal(campos.Order(), ValidadorDeUsuario.Validar(new UsuarioNovo(login, nome, papel, empresa)).Keys.Order());

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
