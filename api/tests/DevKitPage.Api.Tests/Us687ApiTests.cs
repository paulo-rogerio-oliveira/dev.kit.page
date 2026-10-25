using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A US #687 de ponta a ponta pela API: a licença que o admin define e a renovação do dev.kit que a respeita (a regra dos
/// 7 dias, a validade como teto, a revogação), a edição da empresa, o painel de utilidade com o lote novo e o antigo (e a
/// autorização), e a edição de artefato do catálogo — tudo só do admin, menos o painel.
/// </summary>
public sealed class Us687ApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static ConteudoDaLicenca? Ler(LicencaV1? licenca) => LicencaAssinada.Ler(licenca?.Conteudo, Ed25519.ChavePublica(ApiDeTeste.SementeDaLicenca));

    /// <summary>O admin, a empresa e a dev dela (pela adesão), já com a chave da máquina para renovar.</summary>
    private async Task<(HttpClient Admin, HttpClient Ana, int AnaId, EmpresaResumo Empresa)> CenarioAsync()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Acme Software");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@acme.com");
        await _api.MaquinaAsync("maquina-da-ana");
        ana.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["maquina-da-ana"]);
        var id = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == "ana@acme.com").Id;
        return (admin, ana, id, empresa);
    }

    [Fact]
    public async Task A_lista_de_usuarios_traz_a_licenca_e_o_painel_dela_define_previa_e_revoga()
    {
        var (admin, _, anaId, _) = await CenarioAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        var lista = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!;
        var previa = await admin.PostAsJsonAsync($"/api/usuarios/{anaId}/licenca/previa", new DefinicaoDeLicencaV1(ModosDaValidade.AteData, Ate: hoje.AddDays(10)));
        var semMotivo = await admin.PutAsJsonAsync($"/api/usuarios/{anaId}/licenca", new DefinicaoDeLicencaV1(ModosDaValidade.AteData, Ate: hoje.AddDays(10)));
        var torta = await admin.PostAsJsonAsync($"/api/usuarios/{anaId}/licenca/previa", new DefinicaoDeLicencaV1(ModosDaValidade.Somar, Dias: 0));
        var definida = await admin.PutAsJsonAsync($"/api/usuarios/{anaId}/licenca",
            new DefinicaoDeLicencaV1(ModosDaValidade.AteData, Ate: hoje.AddDays(10), Tipo: TiposDaLicencaDoUsuario.Individual, Motivo: "piloto estendido"));
        var revogarSemMotivo = await admin.PostAsJsonAsync($"/api/usuarios/{anaId}/licenca/revogar", new RevogacaoDeLicencaV1(" "));

        Assert.Equal(TiposDaLicencaDoUsuario.Admin, lista.Single(u => u.Login == "admin").Licenca!.Tipo);
        Assert.Equal(SituacoesDaLicenca.Automatica, lista.Single(u => u.Login == "ana@acme.com").Licenca!.Situacao);
        Assert.Equal(10, (await previa.Content.ReadFromJsonAsync<PreviaDeLicencaV1>())!.DiasAPartirDeHoje);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (semMotivo.StatusCode, torta.StatusCode, revogarSemMotivo.StatusCode));
        Assert.Contains("motivo", await semMotivo.Content.ReadAsStringAsync());
        var detalhe = (await definida.Content.ReadFromJsonAsync<DetalheDaLicencaV1>())!;
        Assert.Equal((TiposDaLicencaDoUsuario.Individual, 10, "piloto estendido"), (detalhe.Licenca.Tipo, detalhe.Licenca.DiasRestantes!.Value, detalhe.Historico[0].Motivo));
        Assert.Equal(detalhe.Licenca, (await admin.GetFromJsonAsync<DetalheDaLicencaV1>($"/api/usuarios/{anaId}/licenca"))!.Licenca);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/usuarios/999/licenca")).StatusCode);
    }

    [Fact]
    public async Task A_renovacao_respeita_a_validade_do_admin_a_janela_e_a_revogacao()
    {
        var (admin, ana, anaId, _) = await CenarioAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        // A validade curta do admin é o TETO da licença emitida (e não os 30 dias de sempre).
        (await admin.PutAsJsonAsync($"/api/usuarios/{anaId}/licenca", new DefinicaoDeLicencaV1(ModosDaValidade.AteData, Ate: hoje.AddDays(3), Motivo: "piloto"))).EnsureSuccessStatusCode();
        var curta = (await (await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1())).Content.ReadFromJsonAsync<LicencaV1>())!;
        Assert.Equal(RegrasDeLicenca.FimDoDia(hoje.AddDays(3)), Ler(curta)!.ValidaAte);

        // Logo depois, com a licença curta (a menos de 7 dias do fim), a janela já está aberta: renova.
        var pertoDeVencer = await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1(curta.Conteudo));
        Assert.Equal(HttpStatusCode.OK, pertoDeVencer.StatusCode);

        // A validade longa: a licença volta aos 30 dias, e renovar de novo logo em seguida é cedo demais...
        (await admin.PutAsJsonAsync($"/api/usuarios/{anaId}/licenca", new DefinicaoDeLicencaV1(ModosDaValidade.Somar, 365, Motivo: "contrato anual"))).EnsureSuccessStatusCode();
        var longa = (await (await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1((await pertoDeVencer.Content.ReadFromJsonAsync<LicencaV1>())!.Conteudo)))
            .Content.ReadFromJsonAsync<LicencaV1>())!;
        Assert.Equal(30, Math.Round((Ler(longa)!.ValidaAte - Ler(longa)!.EmitidaEm).TotalDays));
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1(longa.Conteudo))).StatusCode);

        // ... até o admin mudar a licença: a alteração depois da emissão abre a janela ("renova no próximo login").
        (await admin.PutAsJsonAsync($"/api/usuarios/{anaId}/licenca", new DefinicaoDeLicencaV1(ModosDaValidade.SemPrazo, Motivo: "sem prazo"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1(longa.Conteudo))).StatusCode);

        // Revogada: 403, com a mensagem própria, e a lista mostra "Bloqueado: licença".
        (await admin.PostAsJsonAsync($"/api/usuarios/{anaId}/licenca/revogar", new RevogacaoDeLicencaV1("saiu do projeto"))).EnsureSuccessStatusCode();
        var revogada = await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1());
        Assert.Equal(HttpStatusCode.Forbidden, revogada.StatusCode);
        Assert.Contains("revogada", await revogada.Content.ReadAsStringAsync());
        Assert.Equal(SituacoesDaLicenca.Revogada, (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Id == anaId).Licenca!.Situacao);

        // O histórico tem as ações do admin e as renovações do dev.kit.
        var historico = (await admin.GetFromJsonAsync<DetalheDaLicencaV1>($"/api/usuarios/{anaId}/licenca"))!.Historico;
        Assert.Equal(4, historico.Count(h => h.Acao == AcoesNaLicenca.Renovada));
        Assert.Equal(4, historico.Count(h => h.Acao is AcoesNaLicenca.Definida or AcoesNaLicenca.SemPrazo or AcoesNaLicenca.Revogada));
    }

    [Fact]
    public async Task So_o_admin_gerencia_a_licenca_e_edita_a_empresa()
    {
        var (admin, ana, anaId, empresa) = await CenarioAsync();
        var gestor = await _api.GestorAsync(admin, empresa.Id, "gestor.acme");

        Assert.Equal(HttpStatusCode.Forbidden, (await gestor.GetAsync($"/api/usuarios/{anaId}/licenca")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync($"/api/usuarios/{anaId}/licenca", new DefinicaoDeLicencaV1(ModosDaValidade.SemPrazo, Motivo: "eu mesma"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestor.PutAsJsonAsync($"/api/empresas/{empresa.Id}", new EmpresaEditadaV1("X", "Empresarial", 99))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().PutAsJsonAsync($"/api/empresas/{empresa.Id}", new EmpresaEditadaV1("X", "Empresarial", 99))).StatusCode);
    }

    [Fact]
    public async Task A_edicao_da_empresa_valida_o_cnpj_e_os_assentos_em_uso()
    {
        var (admin, _, _, empresa) = await CenarioAsync();

        var cnpjTorto = await admin.PutAsJsonAsync($"/api/empresas/{empresa.Id}", new EmpresaEditadaV1("Acme Software", "Empresarial", 30, Cnpj: "12.345.678/0001-90"));
        var abaixoDoUso = await admin.PutAsJsonAsync($"/api/empresas/{empresa.Id}", new EmpresaEditadaV1("Acme Software", "Empresarial", 0));
        var semAssentoLivre = await admin.PutAsJsonAsync($"/api/empresas/{empresa.Id}", new EmpresaEditadaV1("Acme Software", "Empresarial", 1));
        var editada = await admin.PutAsJsonAsync($"/api/empresas/{empresa.Id}", new EmpresaEditadaV1(
            "Acme Software", "Empresarial", 30, "11.222.333/0001-81", "Software / ERP", "São Paulo", "SP", "gestor@acme.com.br", "(11) 0000-0000", "https://acme.com.br",
            new DateOnly(2027, 3, 16)));

        Assert.Equal(HttpStatusCode.BadRequest, cnpjTorto.StatusCode);
        Assert.Contains("CNPJ", await cnpjTorto.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, abaixoDoUso.StatusCode);
        Assert.Equal(HttpStatusCode.OK, semAssentoLivre.StatusCode); // 1 assento, 1 em uso: no limite, e não abaixo
        var resumo = (await editada.Content.ReadFromJsonAsync<EmpresaResumo>())!;
        Assert.Equal((30, "11222333000181", "Software / ERP"), (resumo.Assentos, resumo.Dados!.Cnpj, resumo.Dados.Segmento));
        Assert.Equal(new DateOnly(2027, 3, 16), (await admin.GetFromJsonAsync<EmpresaResumo[]>("/api/empresas"))!.Single().Dados!.ContratoAte);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/empresas/999", new EmpresaEditadaV1("X", "Empresarial", 1))).StatusCode);
    }

    // Agora, e não antes: o gestor vê só o que veio depois do consentimento da máquina (o registro, segundos antes).
    private static TelemetryEventV1 Evento(string id, string tipo, string detalhe, long? valor = null, string sessao = "s1")
        => new(id, tipo, sessao, 1, valor, detalhe, DateTimeOffset.UtcNow);

    [Fact]
    public async Task O_painel_de_utilidade_recebe_o_lote_novo_e_o_antigo_e_respeita_o_escopo()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Acme");
        var daEmpresa = await _api.MaquinaAsync("m-acme", empresa.CodigoDeAdesao, "Ana");
        var anonima = await _api.MaquinaAsync("m-anonima");
        var gestor = await _api.GestorAsync(admin, empresa.Id, "gestor.acme");

        // O lote NOVO (dev.kit desta US): os tipos novos são gravados, e não ignorados.
        var novo = await daEmpresa.PostAsJsonAsync("/api/telemetria/lote", new TelemetryBatchV1("v1", "m-acme", "5.2.0",
        [
            Evento("n1", TiposDeEvento.SituacaoDaTask, SituacoesDaTask.ParadaSemProcesso),
            Evento("n2", TiposDeEvento.PerguntaAoDev, EstadosDaPergunta.Aberta, sessao: "s2"),
            Evento("n3", TiposDeEvento.DesfechoDoTurno, DesfechosDoTurno.LimiteDeUso),
            Evento("n4", TiposDeEvento.AmostraDaMaquina, "disco-mb=512;memoria-mb=300;limite=memoria", 512, sessao: ""),
        ]));
        // O lote ANTIGO (o dev.kit anterior): continua válido, e os turnos saem da conta de antes.
        var antigo = await anonima.PostAsJsonAsync("/api/telemetria/lote", new TelemetryBatchV1("v1", "m-anonima", "5.0.0",
        [
            Evento("a1", TiposDeEvento.TurnoExecutado, "claude", 1000),
            Evento("a2", TiposDeEvento.TurnoFalhou, "limite-de-uso"),
            Evento("a3", TiposDeEvento.SituacaoDaTask, SituacoesDaTask.ParadaSemProcesso, sessao: "x"),
        ]));

        var doAdmin = (await admin.GetFromJsonAsync<UtilidadeResposta>("/api/dashboard/utilidade"))!;
        var doGestor = (await gestor.GetFromJsonAsync<UtilidadeResposta>("/api/dashboard/utilidade"))!;

        Assert.Equal(new BatchResultV1(4, 4, 0, 0), await novo.Content.ReadFromJsonAsync<BatchResultV1>());
        Assert.Equal(HttpStatusCode.Accepted, antigo.StatusCode);
        Assert.Equal((2, 1, 1), (doAdmin.Saude.TasksParadas.Quantidade, doAdmin.Saude.Perguntas.Quantidade, doAdmin.Saude.Maquinas.Quantidade));
        Assert.Equal(2, doAdmin.TurnosPorDesfecho[^1].LimiteDeUso); // um do desfecho novo, um da conta de antes
        Assert.Equal((1, 1), (doGestor.Saude.TasksParadas.Quantidade, doGestor.TurnosPorDesfecho[^1].LimiteDeUso));
    }

    [Fact]
    public async Task O_painel_de_utilidade_exige_o_painel_e_o_filtro_fora_do_escopo_e_403()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "B");
        await _api.MaquinaAsync("m-b", b.CodigoDeAdesao, "Bia");
        var maquinaDaB = (await admin.GetFromJsonAsync<MaquinaResumo[]>("/api/dashboard/maquinas"))!.Single(m => m.MaquinaId == "m-b").Id;
        var gestorDaA = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var (dev, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "dev@a.com");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/dashboard/utilidade")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/dashboard/utilidade")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorDaA.GetAsync($"/api/dashboard/utilidade?maquina={maquinaDaB}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/dashboard/utilidade?maquina={maquinaDaB}&de=2026-10-01&ate=2026-10-07")).StatusCode);
    }

    private static string Skill(string nome, string conteudo)
        => FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo(nome, $"Sobre {nome}", conteudo, [], [], false, false, false));

    [Fact]
    public async Task A_edicao_de_artefato_lista_busca_compara_descarta_e_anexa()
    {
        var admin = await _api.AdminAsync();
        (await admin.PostAsJsonAsync("/api/catalogos", new CatalogoNovoV1("ferramentas", "Ferramentas do time", null))).EnsureSuccessStatusCode();
        var criado = await admin.PostAsJsonAsync("/api/catalogos/ferramentas/itens", new ItemEditadoV1(TiposDeItem.Skill, Skill("roteiro-do-time", "ng build")));
        var item = (await criado.Content.ReadFromJsonAsync<ItemEditavelV1>())!;
        (await admin.PostAsJsonAsync("/api/catalogos/ferramentas/publicar", new PublicarCatalogoV1())).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/catalogos/ferramentas/itens/{item.Id}", new ItemEditadoV1(TiposDeItem.Skill, Skill("roteiro-do-time", "ng build --configuration production"))))
            .EnsureSuccessStatusCode();

        var achados = (await admin.GetFromJsonAsync<ItemEditavelV1[]>("/api/catalogos/itens?busca=PRODUCTION&escopo=empresa"))!;
        var escopoTorto = await admin.GetAsync("/api/catalogos/itens?escopo=galaxia");
        var diferencas = (await admin.GetFromJsonAsync<DiferencasDoItemV1>($"/api/catalogos/ferramentas/itens/{item.Id}/diferencas"))!;
        var historico = (await admin.GetFromJsonAsync<HistoricoDoItemV1[]>($"/api/catalogos/ferramentas/itens/{item.Id}/historico"))!;

        Assert.Equal(("ferramentas", EscoposDoItem.Empresa, SituacoesDoItem.Alterado), (Assert.Single(achados).Catalogo, achados[0].Escopo, achados[0].Situacao));
        Assert.Equal(HttpStatusCode.BadRequest, escopoTorto.StatusCode);
        Assert.Equal((1, 1, 1), (diferencas.VersaoPublicada, diferencas.Adicionadas, diferencas.Removidas));
        Assert.Contains(historico, h => h is { Acao: AcoesNoItem.Publicado, Versao: 1, Release: "ferramentas-v1" });

        // O anexo por multipart, o download e a remoção.
        using var formulario = new MultipartFormDataContent();
        var arquivo = new ByteArrayContent("passo a passo"u8.ToArray());
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        formulario.Add(arquivo, "arquivo", "roteiro.md");
        var anexado = await admin.PostAsync($"/api/catalogos/ferramentas/itens/{item.Id}/anexos", formulario);
        var anexo = (await anexado.Content.ReadFromJsonAsync<AnexoDoItemV1>())!;
        Assert.Equal((HttpStatusCode.Created, "roteiro.md"), (anexado.StatusCode, anexo.Nome));
        Assert.Equal("passo a passo", await admin.GetStringAsync($"/api/catalogos/ferramentas/itens/{item.Id}/anexos/{anexo.Id}"));
        Assert.Single((await admin.GetFromJsonAsync<AnexoDoItemV1[]>($"/api/catalogos/ferramentas/itens/{item.Id}/anexos"))!);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/catalogos/ferramentas/itens/{item.Id}/anexos/{anexo.Id}")).StatusCode);

        // Descartar o rascunho volta à versão publicada.
        var descartado = (await (await admin.PostAsync($"/api/catalogos/ferramentas/itens/{item.Id}/descartar", null)).Content.ReadFromJsonAsync<ItemEditavelV1>())!;
        Assert.Equal(SituacoesDoItem.Publicado, descartado.Situacao);
    }

    [Fact]
    public async Task A_rule_de_impacto_e_recusada_no_basico_e_so_o_admin_edita_o_artefato()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Acme");
        var gestor = await _api.GestorAsync(admin, empresa.Id, "gestor.acme");
        const string impacto = """{"key":"licenca","description":"Licença","content":"GATILHO: licença","tipo":"impacto"}""";

        var noBasico = await admin.PostAsJsonAsync("/api/catalogos/basico/itens", new ItemEditadoV1(TiposDeItem.Plan, impacto));

        Assert.Equal(HttpStatusCode.BadRequest, noBasico.StatusCode);
        Assert.Contains("Rule de impacto", await noBasico.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await gestor.GetAsync("/api/catalogos/itens")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().PostAsync("/api/catalogos/basico/itens/1/descartar", null)).StatusCode);
    }
}
