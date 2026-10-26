using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A US #687 na infraestrutura, sobre a base de verdade: o script do Azure SQL, as fotos do "Saúde agora" na ingestão (o
/// mais novo vence, o reenvio não conta de novo), cada indicador do painel de utilidade (e o período vazio, e o recorte do
/// gestor), a licença definida pelo admin com o histórico, a edição da empresa e a edição de artefato do catálogo.
/// </summary>
public sealed class Us687Tests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new();

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<T> Com<T>(Func<IServiceProvider, Task<T>> acao) => _base.ComAsync(acao);

    private DateTimeOffset Agora => _base.Relogio.Agora;

    private Task<BatchResultV1> EnviarAsync(int maquina, params TelemetryEventV1[] eventos)
        => Com(sp => sp.GetRequiredService<IIngestaoDeTelemetria>().RegistrarAsync(maquina, new TelemetryBatchV1("v1", "x", "1.5.0", eventos), default));

    private static TelemetryEventV1 Evento(string id, string tipo, DateTimeOffset em, string detalhe, long? valor = null, string sessao = "s1")
        => new(id, tipo, sessao, 1, valor, detalhe, em);

    private Task<UtilidadeResposta> UtilidadeAsync(EscopoDoPainel? escopo = null, int dias = 30)
        => Com(sp => sp.GetRequiredService<IConsultasDoPainel>().UtilidadeAsync(escopo ?? EscopoDoPainel.Tudo,
            new Periodo(DateOnly.FromDateTime(Agora.UtcDateTime).AddDays(-(dias - 1)), DateOnly.FromDateTime(Agora.UtcDateTime)), null, Agora, default));

    [Fact]
    public async Task O_admin_ve_o_teste_gratis_pelos_dias_configurados_os_mesmos_da_renovacao()
    {
        // Task 388: com Licencas:TrialDias = 7, a lista, o detalhe e a prévia contam 7 dias do cadastro — não os 14 do padrão.
        await using var base7 = new BaseDeTeste(new() { ["Licencas:TrialDias"] = "7" });
        await base7.InicializarAsync();
        var trial = (await base7.ComAsync(sp => sp.GetRequiredService<IGestaoDeUsuarios>().CriarAsync(new UsuarioNovo("teste", "Teste", Papeis.Dev, null), default))).Valor!.Usuario;
        var fim = RegrasDeLicenca.FimDoTeste(base7.Relogio.Agora.UtcDateTime, 7);

        var naLista = (await base7.ComAsync(sp => sp.GetRequiredService<IGestaoDeUsuarios>().ListarAsync(default))).Single(u => u.Login == "teste").Licenca!;
        var detalhe = (await base7.ComAsync(sp => sp.GetRequiredService<ILicencasDosUsuarios>().DetalheAsync(trial.Id, default))).Valor!.Licenca;
        var previa = (await base7.ComAsync(sp => sp.GetRequiredService<ILicencasDosUsuarios>()
            .PreviaAsync(trial.Id, new DefinicaoDeLicencaV1(ModosDaValidade.Somar, 3), default))).Valor!;

        Assert.Equal((TiposDaLicencaDoUsuario.Trial, fim, 7), (naLista.Tipo, naLista.ValidaAte!.Value, naLista.DiasRestantes!.Value));
        Assert.Equal(fim, detalhe.ValidaAte);
        Assert.Equal(RegrasDeLicenca.FimDoDia(DateOnly.FromDateTime(fim.UtcDateTime).AddDays(3)), previa.ValidaAte); // somar parte do fim do teste
    }

    [Fact]
    public void Script_do_azure_sql_cria_as_tabelas_e_as_colunas_da_us()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us687.sql");

        foreach (var tabela in new[] { "HistoricosDeLicenca", "FotosDeEstado", "HistoricosDeItemDoCatalogo", "AnexosDeItemDoCatalogo" })
        {
            Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
            foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
                Assert.Contains(coluna, script);
        }

        var novasDeUsuarios = new[] { "LicencaValidaAteUtc", "LicencaSemPrazo", "LicencaTipo", "LicencaRevogadaEmUtc", "LicencaAlteradaEmUtc" };
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("Usuarios").Where(c => novasDeUsuarios.Any(n => c.StartsWith($"[{n}]", StringComparison.Ordinal))))
            Assert.Contains(coluna, script);
        var novasDeEmpresas = new[] { "Cnpj", "Segmento", "Cidade", "Uf", "Contato", "Telefone", "Site", "ContratoAteUtc", "AlteradaEmUtc", "AlteradaPor" };
        var colunasDeEmpresas = BaseDeTeste.ColunasNoSqlServer("Empresas").Where(c => novasDeEmpresas.Any(n => c.StartsWith($"[{n}]", StringComparison.Ordinal))).ToList();
        Assert.Equal(novasDeEmpresas.Length, colunasDeEmpresas.Count);
        foreach (var coluna in colunasDeEmpresas)
            Assert.Contains(coluna, script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_FotosDeEstado_MaquinaId_Tipo_Chave]", script);
    }

    [Fact]
    public async Task A_foto_guarda_o_mais_novo_e_o_reenvio_nao_conta_de_novo()
    {
        var maquina = await _base.MaquinaAsync("m-1");

        var primeiro = await EnviarAsync(maquina,
            Evento("e1", TiposDeEvento.SituacaoDaTask, Agora.AddHours(-3), SituacoesDaTask.Executando),
            Evento("e2", TiposDeEvento.SituacaoDaTask, Agora.AddHours(-2), SituacoesDaTask.ParadaSemProcesso, 60),
            Evento("e3", TiposDeEvento.AmostraDaMaquina, Agora.AddHours(-1), "torta", sessao: ""));
        // O reenvio do mesmo lote, e um evento ANTIGO que chega atrasado: nenhum volta a foto para trás.
        var reenvio = await EnviarAsync(maquina, Evento("e2", TiposDeEvento.SituacaoDaTask, Agora.AddHours(-2), SituacoesDaTask.ParadaSemProcesso, 60));
        await EnviarAsync(maquina, Evento("e0", TiposDeEvento.SituacaoDaTask, Agora.AddHours(-5), SituacoesDaTask.EsperandoDev));

        var fotos = await Com(sp => sp.GetRequiredService<DevKitPageDb>().FotosDeEstado.AsNoTracking().ToListAsync());
        Assert.Equal((3, 0), (primeiro.Novos, primeiro.Ignorados));
        Assert.Equal((0, 1), (reenvio.Novos, reenvio.Duplicados));
        var foto = Assert.Single(fotos); // a amostra torta conta no total e não cria foto
        Assert.Equal((TiposDeEvento.SituacaoDaTask, "s1", SituacoesDaTask.ParadaSemProcesso, "e2"), (foto.Tipo, foto.Chave, foto.Detalhe, foto.EventId));
    }

    [Fact]
    public async Task O_painel_de_utilidade_mostra_a_saude_o_resultado_os_turnos_e_onde_travam()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        var ontem = Agora.AddDays(-1);
        await EnviarAsync(maquina,
            // Saúde agora: duas tasks paradas (uma nas últimas 24 h), uma pergunta aberta e uma respondida, o fechamento
            // que falhou e o que passou depois de falhar, a máquina no limite.
            Evento("p1", TiposDeEvento.SituacaoDaTask, Agora.AddDays(-10), SituacoesDaTask.ParadaSemProcesso, sessao: "t1"),
            Evento("p2", TiposDeEvento.SituacaoDaTask, Agora.AddHours(-2), SituacoesDaTask.ParadaSemProcesso, sessao: "t2"),
            Evento("p3", TiposDeEvento.SituacaoDaTask, Agora.AddHours(-1), SituacoesDaTask.Executando, sessao: "t3"),
            Evento("q1", TiposDeEvento.PerguntaAoDev, Agora.AddDays(-3), EstadosDaPergunta.Aberta, sessao: "t4"),
            Evento("q2", TiposDeEvento.PerguntaAoDev, Agora.AddHours(-5), EstadosDaPergunta.Respondida, 30, sessao: "t5"),
            Evento("f1", TiposDeEvento.FechamentoDaEntrega, Agora.AddHours(-6), "falhou", 669, sessao: "fa"),
            Evento("f2", TiposDeEvento.FechamentoDaEntrega, Agora.AddHours(-8), "falhou", 585, sessao: "fb"),
            Evento("f3", TiposDeEvento.FechamentoDaEntrega, Agora.AddHours(-4), "ok", 585, sessao: "fc"),
            Evento("a1", TiposDeEvento.AmostraDaMaquina, Agora.AddMinutes(-30), "disco-mb=8;memoria-mb=124;limite=disco", 8, sessao: ""),
            // Resultado: a sessão aprovada na primeira, com duas avaliações; os turnos do dia pelo desfecho.
            Evento("r1", TiposDeEvento.SessaoIniciada, ontem.AddHours(-2), "5.1", sessao: "s1"),
            Evento("r2", TiposDeEvento.ObjetivoAvaliado, ontem.AddHours(-1), "", 82, sessao: "s1"),
            Evento("r3", TiposDeEvento.ObjetivoAvaliado, ontem.AddMinutes(-30), "", 96, sessao: "s1"),
            Evento("r4", TiposDeEvento.ObjetivoCumprido, ontem.AddMinutes(-20), "", sessao: "s1"),
            Evento("r5", TiposDeEvento.RevisaoHumana, ontem, "aprovada", 3_600_000, sessao: "s1"),
            Evento("t1", TiposDeEvento.TurnoExecutado, ontem.AddHours(-1), "claude", 1000, sessao: "s1"),
            Evento("t2", TiposDeEvento.TurnoExecutado, ontem.AddMinutes(-50), "claude", 1000, sessao: "s1"),
            Evento("d1", TiposDeEvento.DesfechoDoTurno, ontem.AddHours(-1), DesfechosDoTurno.Trabalhou, sessao: "s1"),
            Evento("d2", TiposDeEvento.DesfechoDoTurno, ontem.AddMinutes(-50), DesfechosDoTurno.EsperouDev, sessao: "s1"),
            // Onde travam: duas sugestões da mesma causa (a mais nova pendente) e uma promovida com a US.
            Evento("g1", TiposDeEvento.SugestaoDeImpacto, Agora.AddDays(-4), "Esperou processo que morreu|sugestao-1|descartada|", sessao: ""),
            Evento("g2", TiposDeEvento.SugestaoDeImpacto, Agora.AddDays(-2), "Esperou processo que morreu|sugestao-3|pendente|", sessao: ""),
            Evento("g3", TiposDeEvento.SugestaoDeImpacto, Agora.AddDays(-1), "Pergunta sem chegar ao dev|sugestao-2|promovida|#568", sessao: ""));

        var painel = await UtilidadeAsync();

        Assert.Equal((2, 1), (painel.Saude.TasksParadas.Quantidade, painel.Saude.TasksParadas.Novos24h));
        Assert.Equal(Agora.AddDays(-10), painel.Saude.TasksParadas.MaisAntigo);
        Assert.Equal((1, Agora.AddDays(-3)), (painel.Saude.Perguntas.Quantidade, painel.Saude.Perguntas.MaisAntigo));
        Assert.Equal(new[] { "#669" }, painel.Saude.Fechamentos.Itens); // o #585 passou depois de falhar
        Assert.Equal("máquina m-1: disco 8 MB · memória 124 MB livres", Assert.Single(painel.Saude.Maquinas.Itens));

        Assert.Equal((1L, 1L, 100d, 89d, 2d), (painel.Resultado.Aprovadas, painel.Resultado.Integradas, painel.Resultado.PercentualNaPrimeira!.Value,
            painel.Resultado.NotaMedia!.Value, painel.Resultado.RodadasAteANota!.Value));
        Assert.Equal((120d, 50d, 2d), (painel.Resultado.MedianaAteAprovacaoMinutos!.Value, painel.Resultado.PercentualEsperandoDev!.Value, painel.Resultado.TurnosPorEntrega!.Value));
        Assert.Equal(30, painel.TurnosPorDesfecho.Count);
        Assert.Equal(new TurnosDoDiaV1(DateOnly.FromDateTime(ontem.UtcDateTime), 1, 1, 0, 0), painel.TurnosPorDesfecho[^2]);

        Assert.Equal(new[] { ("Esperou processo que morreu", 2L, "sugestão #3"), ("Pergunta sem chegar ao dev", 1L, "corrigido #568") },
            painel.OndeTravam.Select(c => (c.Causa, c.Vezes, c.Selo)));
    }

    [Fact]
    public async Task O_periodo_vazio_e_o_gestor_sem_maquina_que_consentiu_tem_tudo_zerado()
    {
        var empresa = await Com(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova("Acme", "Empresarial", 5), default));
        var anonima = await _base.MaquinaAsync("m-anonima");
        await EnviarAsync(anonima, Evento("p1", TiposDeEvento.SituacaoDaTask, Agora.AddHours(-1), SituacoesDaTask.ParadaSemProcesso));

        var doGestor = await UtilidadeAsync(new EscopoDoPainel(empresa.Id));
        var vazio = await new BaseDeTesteVazia().UtilidadeAsync();

        Assert.Equal(0, doGestor.Saude.TasksParadas.Quantidade);
        Assert.Equal(1, (await UtilidadeAsync()).Saude.TasksParadas.Quantidade);
        Assert.Equal((0L, (double?)null, (double?)null), (vazio.Resultado.Aprovadas, vazio.Resultado.PercentualNaPrimeira, vazio.Resultado.MedianaAteAprovacaoMinutos));
        Assert.Empty(vazio.OndeTravam);
        Assert.All(vazio.TurnosPorDesfecho, d => Assert.Equal(0, d.Trabalhou + d.EsperouDev + d.LimiteDeUso + d.Falhou));
    }

    /// <summary>Uma base sem nada além da semente: o período vazio de verdade.</summary>
    private sealed class BaseDeTesteVazia
    {
        public async Task<UtilidadeResposta> UtilidadeAsync()
        {
            await using var vazia = new BaseDeTeste();
            await vazia.InicializarAsync();
            var hoje = DateOnly.FromDateTime(vazia.Relogio.Agora.UtcDateTime);
            return await vazia.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>()
                .UtilidadeAsync(EscopoDoPainel.Tudo, new Periodo(hoje.AddDays(-6), hoje), null, vazia.Relogio.Agora, default));
        }
    }

    [Fact]
    public async Task O_admin_define_a_validade_e_a_renovacao_a_respeita_com_o_historico()
    {
        var empresa = await Com(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova("Acme", "Empresarial", 5), default));
        var ana = (await Com(sp => sp.GetRequiredService<IGestaoDeUsuarios>().CriarAsync(new UsuarioNovo("ana", "Ana Lima", Papeis.Dev, empresa.Id), default))).Valor!.Usuario;
        var licencas = (Func<ILicencasDosUsuarios, Task<ResultadoDaGestao<DetalheDaLicencaV1>>> f) => Com(sp => f(sp.GetRequiredService<ILicencasDosUsuarios>()));

        var antes = (await licencas(l => l.DetalheAsync(ana.Id, default))).Valor!;
        var previa = (await Com(sp => sp.GetRequiredService<ILicencasDosUsuarios>()
            .PreviaAsync(ana.Id, new DefinicaoDeLicencaV1(ModosDaValidade.Somar, 30, Tipo: TiposDaLicencaDoUsuario.Trial), default))).Valor!;
        _base.Relogio.Agora = Agora.AddMinutes(1);
        var definida = (await licencas(l => l.DefinirAsync(ana.Id, new DefinicaoDeLicencaV1(ModosDaValidade.Somar, 30, Tipo: TiposDaLicencaDoUsuario.Trial, Motivo: "piloto Acme"), "admin", default))).Valor!;
        _base.Relogio.Agora = Agora.AddMinutes(1);
        var revogada = (await licencas(l => l.RevogarAsync(ana.Id, "saiu da empresa", "admin", default))).Valor!;

        Assert.Equal((SituacoesDaLicenca.Automatica, false), (antes.Licenca.Situacao, antes.Licenca.DefinidaPeloAdmin));
        Assert.Equal((RegrasDeLicenca.FimDoDia(new DateOnly(2026, 11, 2)), 30), (previa.ValidaAte!.Value, previa.DiasAPartirDeHoje!.Value));
        Assert.Contains("renova no próximo login", previa.Mensagem);
        Assert.Equal((TiposDaLicencaDoUsuario.Trial, SituacoesDaLicenca.Valida, true), (definida.Licenca.Tipo, definida.Licenca.Situacao, definida.Licenca.DefinidaPeloAdmin));
        Assert.Equal(SituacoesDaLicenca.Revogada, revogada.Licenca.Situacao);
        Assert.Equal(new[] { (AcoesNaLicenca.Revogada, "saiu da empresa"), (AcoesNaLicenca.Definida, "piloto Acme"), (AcoesNaLicenca.Criada, "") },
            revogada.Historico.Select(h => (h.Acao, h.Motivo)));
        Assert.Equal(SituacoesDaLicenca.Revogada,
            (await Com(sp => sp.GetRequiredService<IGestaoDeUsuarios>().ListarAsync(default))).Single(u => u.Login == "ana").Licenca!.Situacao);

        var admin = (await Com(sp => sp.GetRequiredService<IGestaoDeUsuarios>().ListarAsync(default))).Single(u => u.Login == "admin");
        Assert.Equal(FalhaNaGestao.Recusada, (await licencas(l => l.RevogarAsync(admin.Id, "x", "admin", default))).Falha);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await licencas(l => l.DetalheAsync(999, default))).Falha);
    }

    [Fact]
    public async Task A_edicao_da_empresa_grava_os_dados_e_nao_desce_dos_assentos_em_uso()
    {
        var empresa = await Com(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova("Acme", "Empresarial", 5), default));
        foreach (var login in new[] { "dev1", "dev2" })
            await Com(sp => sp.GetRequiredService<IGestaoDeUsuarios>().CriarAsync(new UsuarioNovo(login, login, Papeis.Dev, empresa.Id), default));
        Task<ResultadoDaGestao<EmpresaResumo>> Editar(int assentos) => Com(sp => sp.GetRequiredService<IEmpresas>().EditarAsync(empresa.Id,
            new EmpresaEditadaV1("Acme Software", "Empresarial", assentos, "11.222.333/0001-81", "Software / ERP", "São Paulo", "sp", "gestor@acme.com.br", "", "https://acme.com.br",
                new DateOnly(2027, 3, 16)), "admin", default));

        var editada = (await Editar(30)).Valor!;
        var abaixo = await Editar(1);

        Assert.Equal(("Acme Software", 30, 2), (editada.Nome, editada.Assentos, editada.AssentosEmUso));
        Assert.Equal(("11222333000181", "SP", new DateOnly(2027, 3, 16), "admin"), (editada.Dados!.Cnpj, editada.Dados.Uf, editada.Dados.ContratoAte, editada.Dados.AlteradaPor));
        Assert.Equal(Agora, editada.Dados.AlteradaEm);
        Assert.Equal(FalhaNaGestao.Recusada, abaixo.Falha);
        Assert.Contains("o mínimo é 2", abaixo.Mensagem);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Com(sp => sp.GetRequiredService<IEmpresas>().EditarAsync(999,
            new EmpresaEditadaV1("X", "Empresarial", 1), "admin", default))).Falha);

        // O contrato é a validade da licença dos devs dela que o admin não definiu um a um.
        var dev = (await Com(sp => sp.GetRequiredService<IGestaoDeUsuarios>().ListarAsync(default))).Single(u => u.Login == "dev1");
        Assert.Equal((TiposDaLicencaDoUsuario.Empresa, RegrasDeLicenca.FimDoDia(new DateOnly(2027, 3, 16))), (dev.Licenca!.Tipo, dev.Licenca.ValidaAte!.Value));
    }

    private static string Skill(string nome, string conteudo)
        => FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo(nome, $"Sobre {nome}", conteudo, [], [], false, false, false));

    private const string RuleDeImpacto = """{"key":"licenca","description":"Licença","content":"GATILHO: licença","repository":"","onlyWithWorkItem":false,"isRequired":false,"isAgentInstruction":false,"tipo":"impacto"}""";

    private Task<ResultadoDaGestao<ItemEditavelV1>> SalvarAsync(string slug, string tipo, string conteudo, long? id = null)
    {
        var (conferido, erro) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(tipo, conteudo));
        Assert.True(conferido is not null, erro);
        return Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().SalvarItemAsync(slug, id, conferido, null, "admin", default));
    }

    [Fact]
    public async Task A_rule_de_impacto_nunca_vai_para_o_basico_e_vai_para_o_catalogo_da_empresa()
    {
        await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().CriarAsync(new CatalogoNovoV1("acme", "Acme", null), default));

        var noBasico = await SalvarAsync(Catalogos.Basico, TiposDeItem.Plan, RuleDeImpacto);
        var naEmpresa = await SalvarAsync("acme", TiposDeItem.Plan, RuleDeImpacto);

        Assert.Equal(FalhaNaGestao.Recusada, noBasico.Falha);
        Assert.Contains("nunca vai para o catálogo básico", noBasico.Mensagem);
        Assert.Equal((EscoposDoItem.Empresa, true, "acme"), (naEmpresa.Valor!.Escopo, naEmpresa.Valor.RuleDeImpacto, naEmpresa.Valor.Catalogo));
    }

    [Fact]
    public async Task Diferencas_historico_descarte_e_anexos_do_item()
    {
        var gestao = (Func<IGestaoDeCatalogos, Task<ResultadoDaGestao<ItemEditavelV1>>> f) => Com(sp => f(sp.GetRequiredService<IGestaoDeCatalogos>()));
        var item = (await SalvarAsync(Catalogos.Basico, TiposDeItem.Skill, Skill("compilar-net-framework", "1. Ache o MSBuild.\n2. Compile o projeto."))).Valor!;
        var publicada = await Com(sp => sp.GetRequiredService<IPublicacaoDeCatalogos>().PublicarAsync(Catalogos.Basico, null, "admin", default));
        Assert.True(publicada.Feita is not null, publicada.Mensagem);

        // O rascunho da v2 (salvo duas vezes seguidas: uma linha só no histórico) e as diferenças com a v1.
        await SalvarAsync(Catalogos.Basico, TiposDeItem.Skill, Skill("compilar-net-framework", "1. Ache o MSBuild.\n2. Compile só o que mudou."), item.Id);
        await SalvarAsync(Catalogos.Basico, TiposDeItem.Skill, Skill("compilar-net-framework", "1. Ache o MSBuild.\n2. Compile só o que mudou.\n3. -m:1"), item.Id);
        var diferencas = (await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().DiferencasAsync(Catalogos.Basico, item.Id, default))).Valor!;
        var historico = (await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().HistoricoAsync(Catalogos.Basico, item.Id, default))).Valor!;

        Assert.Equal((1, 2, 1), (diferencas.VersaoPublicada, diferencas.Adicionadas, diferencas.Removidas));
        Assert.StartsWith("---\nname: compilar-net-framework", diferencas.Rascunho);
        Assert.Equal(new[] { AcoesNoItem.Rascunho, AcoesNoItem.Publicado }, historico.Select(h => h.Acao));
        Assert.Equal("catalogo-v1", historico[1].Release);

        // A busca acha pelo CONTEÚDO, e o escopo filtra.
        var achados = await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().TodosOsItensAsync("-m:1", null, EscoposDoItem.Base, default));
        Assert.Equal((item.Id, SituacoesDoItem.Alterado, EscoposDoItem.Base), (Assert.Single(achados).Id, achados[0].Situacao, achados[0].Escopo));
        Assert.Empty(await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().TodosOsItensAsync("-m:1", null, EscoposDoItem.Empresa, default)));

        // Descartar volta à v1 publicada.
        var descartado = (await gestao(g => g.DescartarRascunhoAsync(Catalogos.Basico, item.Id, "admin", default))).Valor!;
        Assert.Equal(SituacoesDoItem.Publicado, descartado.Situacao);
        Assert.Contains("2. Compile o projeto.", descartado.Conteudo);

        // Anexos: o arquivo fica no site, com o limite de tamanho.
        var anexo = (await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().AnexarAsync(Catalogos.Basico, item.Id, @"C:\tmp\exemplo.log", "text/plain", "ok"u8.ToArray(), "admin", default))).Valor!;
        var grande = await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().AnexarAsync(Catalogos.Basico, item.Id, "grande.bin", "", new byte[AnexosDoItem.TamanhoMaximo + 1], "admin", default));
        var baixado = await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().AnexoAsync(Catalogos.Basico, item.Id, anexo.Id, default));
        Assert.Equal(("exemplo.log", 2L), (anexo.Nome, anexo.TamanhoBytes));
        Assert.Equal("ok", System.Text.Encoding.UTF8.GetString(baixado!.Conteudo));
        Assert.Equal(FalhaNaGestao.Recusada, grande.Falha);
        Assert.NotNull((await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().RemoverAnexoAsync(Catalogos.Basico, item.Id, anexo.Id, default))).Valor);
        Assert.Empty((await Com(sp => sp.GetRequiredService<IGestaoDeCatalogos>().AnexosAsync(Catalogos.Basico, item.Id, default))).Valor!);

        // O nunca publicado: descartar o rascunho apaga o item.
        var novo = (await SalvarAsync(Catalogos.Basico, TiposDeItem.Skill, Skill("so-rascunho", "x"))).Valor!;
        var apagado = await gestao(g => g.DescartarRascunhoAsync(Catalogos.Basico, novo.Id, "admin", default));
        Assert.Equal((FalhaNaGestao.Nenhuma, (ItemEditavelV1?)null), (apagado.Falha, apagado.Valor));
    }
}
