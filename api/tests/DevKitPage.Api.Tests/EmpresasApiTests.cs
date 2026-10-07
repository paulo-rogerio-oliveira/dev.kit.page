using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O plano empresarial de ponta a ponta (US #381): o admin cria as empresas e convida os gestores; o
/// colaborador adere no registro da máquina; o gestor da empresa A NUNCA lê a empresa B (nem pela
/// lista, nem pelo filtro, nem pelos erros, nem pela exportação); a máquina sem consentimento entra só
/// nos totais; e cada exportação fica na trilha de auditoria.
/// </summary>
public sealed class EmpresasApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static readonly DateTimeOffset Hoje = DateTimeOffset.UtcNow;

    private static TelemetryBatchV1 Lote(string maquina, params TelemetryEventV1[] eventos) => new("v1", maquina, "1.4.0", eventos);

    private static TelemetryEventV1 Turno(string id) => new(id, TiposDeEvento.TurnoExecutado, "s1", 1, 1000, "claude", Hoje);

    private static TelemetryEventV1 Excecao(string id, string assinatura)
        => new(id, TiposDeEvento.ExcecaoNaoClassificada, "s1", 1, null, assinatura, Hoje, "System.Exception: falhou\n   at GitKit.X.Y() linha 1", assinatura);

    /// <summary>Duas empresas, um gestor em cada, e uma máquina que aderiu a cada uma — com um turno e um erro próprios.</summary>
    private async Task<(HttpClient Admin, HttpClient GestorA, HttpClient GestorB, EmpresaResumo A, EmpresaResumo B)> CenarioAsync()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var gestorA = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var gestorB = await _api.GestorAsync(admin, b.Id, "gestor.b");

        var maqA = await _api.MaquinaAsync("maq-a", a.CodigoDeAdesao.ToLowerInvariant(), "Ana"); // o código é aceito em minúsculas
        var maqB = await _api.MaquinaAsync("maq-b", b.CodigoDeAdesao, "Bruno");
        (await maqA.PostAsJsonAsync("/api/telemetria/lote", Lote("maq-a", Turno("a1"), Excecao("ea", "erro-de-a")))).EnsureSuccessStatusCode();
        (await maqB.PostAsJsonAsync("/api/telemetria/lote", Lote("maq-b", Turno("b1"), Turno("b2"), Excecao("eb", "erro-de-b")))).EnsureSuccessStatusCode();
        return (admin, gestorA, gestorB, a, b);
    }

    private static async Task<int> IdDaMaquina(HttpClient admin, string maquinaId)
        => (await admin.GetFromJsonAsync<MaquinaResumo[]>("/api/dashboard/maquinas"))!.Single(m => m.MaquinaId == maquinaId).Id;

    [Fact]
    public async Task Gestor_da_empresa_A_nao_le_maquinas_erros_nem_a_exportacao_da_empresa_B()
    {
        var (admin, gestorA, _, _, _) = await CenarioAsync();
        var maquinaDeB = await IdDaMaquina(admin, "maq-b");
        var erroDeB = (await admin.GetFromJsonAsync<Pagina<GrupoDeErroResumo>>("/api/dashboard/erros"))!.Itens.Single(g => g.Assinatura == "erro-de-b").Id;

        var maquinas = (await gestorA.GetFromJsonAsync<MaquinaResumo[]>("/api/dashboard/maquinas"))!;
        var quantidade = (await gestorA.GetFromJsonAsync<QuantidadeResposta>("/api/dashboard/quantidade"))!;
        var erros = (await gestorA.GetFromJsonAsync<Pagina<GrupoDeErroResumo>>("/api/dashboard/erros"))!;
        var colaboradores = (await gestorA.GetFromJsonAsync<ColaboradorResumo[]>("/api/dashboard/colaboradores"))!;
        var exportado = (await gestorA.GetFromJsonAsync<LinhaExportada[]>("/api/dashboard/exportar?formato=json"))!;

        Assert.Equal("maq-a", Assert.Single(maquinas).MaquinaId);
        Assert.Equal((1L, 1L, 1L), (quantidade.Turnos, quantidade.MaquinasAtivas, quantidade.MaquinasRegistradas));
        Assert.Equal("erro-de-a", Assert.Single(erros.Itens).Assinatura);
        Assert.Equal(("Ana", "Empresa A"), (Assert.Single(colaboradores).Colaborador, colaboradores[0].Empresa));
        Assert.All(exportado, l => Assert.Equal("Ana", l.Colaborador));

        // Pedir o dado de B pelo id é 403 — o filtro não serve para sondar outra empresa.
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync($"/api/dashboard/quantidade?maquina={maquinaDeB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync($"/api/dashboard/eventos?maquina={maquinaDeB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync($"/api/dashboard/exportar?maquina={maquinaDeB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync($"/api/dashboard/erros/{erroDeB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync($"/api/dashboard/erros/{erroDeB}/exportar")).StatusCode);
    }

    [Fact]
    public async Task Admin_ve_tudo_e_o_gestor_nao_administra()
    {
        var (admin, gestorA, _, a, _) = await CenarioAsync();

        var maquinas = (await admin.GetFromJsonAsync<MaquinaResumo[]>("/api/dashboard/maquinas"))!;
        var empresas = (await admin.GetFromJsonAsync<EmpresaResumo[]>("/api/empresas"))!;
        var colaboradores = (await admin.GetFromJsonAsync<ColaboradorResumo[]>("/api/dashboard/colaboradores"))!;

        Assert.Equal(new[] { "maq-a", "maq-b" }, maquinas.Select(m => m.MaquinaId).Order());
        Assert.Equal(1, empresas.Single(e => e.Id == a.Id).Colaboradores);
        Assert.Equal(new[] { "Ana", "Bruno" }, colaboradores.Select(c => c.Colaborador));

        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync("/api/empresas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.PostAsJsonAsync("/api/empresas", new EmpresaNova("X", "Y", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync("/api/dashboard/demonstracoes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.PutAsJsonAsync("/api/dashboard/erros/1/estado", new AlterarEstadoDoGrupo(EstadosDoGrupo.Visto, null))).StatusCode);
    }

    [Fact]
    public async Task Login_do_gestor_traz_o_papel_e_a_empresa_e_exige_a_troca_de_senha()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var criado = (await (await admin.PostAsJsonAsync($"/api/empresas/{a.Id}/gestores", new GestorNovo("gestor.a"))).Content.ReadFromJsonAsync<GestorCriado>())!;

        var login = (await (await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("gestor.a", criado.SenhaInicial))).Content.ReadFromJsonAsync<LoginResponse>())!;
        var repetido = await admin.PostAsJsonAsync($"/api/empresas/{a.Id}/gestores", new GestorNovo("gestor.a"));
        var semEmpresa = await admin.PostAsJsonAsync("/api/empresas/999/gestores", new GestorNovo("outro"));

        Assert.Equal((Papeis.Gestor, "Empresa A", true, false), (login.Papel, login.Empresa, login.DeveTrocarSenha, login.EhAdmin));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, semEmpresa.StatusCode);
    }

    [Fact]
    public async Task Maquina_da_empresa_sem_consentimento_entra_so_nos_totais()
    {
        var (_, gestorA, _, a, _) = await CenarioAsync();
        var semConsentimento = await _api.MaquinaAsync("maq-sem");
        (await semConsentimento.PostAsJsonAsync("/api/telemetria/lote", Lote("maq-sem", Turno("s1"), Turno("s2"), Turno("s3")))).EnsureSuccessStatusCode();
        using (var escopo = _api.Services.CreateScope())
        {
            // O vínculo sem o consentimento (ex.: o colaborador o retirou): a máquina é da empresa, mas não aparece.
            var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
            var maquina = await db.Maquinas.SingleAsync(m => m.MaquinaId == "maq-sem");
            maquina.EmpresaId = a.Id;
            await db.SaveChangesAsync();
        }

        var quantidade = (await gestorA.GetFromJsonAsync<QuantidadeResposta>("/api/dashboard/quantidade"))!;
        var maquinas = (await gestorA.GetFromJsonAsync<MaquinaResumo[]>("/api/dashboard/maquinas"))!;
        var eventos = (await gestorA.GetFromJsonAsync<Pagina<EventoDoLog>>("/api/dashboard/eventos"))!;
        var exportado = (await gestorA.GetFromJsonAsync<LinhaExportada[]>("/api/dashboard/exportar?formato=json"))!;

        Assert.Equal(4L, quantidade.Turnos); // 1 da Ana + 3 da máquina sem consentimento
        Assert.Equal("maq-a", Assert.Single(maquinas).MaquinaId);
        Assert.All(eventos.Itens, e => Assert.Equal("máquina maq-a", e.Apelido));
        Assert.Equal(1L, exportado.Sum(l => l.Turnos));
    }

    [Fact]
    public async Task Exportacao_em_csv_grava_a_auditoria_e_neutraliza_formula()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var gestorA = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var maquina = await _api.MaquinaAsync("maq-a", a.CodigoDeAdesao, "=HYPERLINK(\"x\")");
        (await maquina.PostAsJsonAsync("/api/telemetria/lote", Lote("maq-a", Turno("a1")))).EnsureSuccessStatusCode();

        var resposta = await gestorA.GetAsync("/api/dashboard/exportar?formato=csv");
        var csv = await resposta.Content.ReadAsStringAsync();
        var invalido = await gestorA.GetAsync("/api/dashboard/exportar?formato=xml");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("text/csv", resposta.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("colaborador;maquina;empresa;dia;sessoes;turnos", csv.TrimStart('﻿'));
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);

        using var escopo = _api.Services.CreateScope();
        var trilha = await escopo.ServiceProvider.GetRequiredService<DevKitPageDb>().AcessosAosDados.AsNoTracking().ToListAsync();
        var acesso = Assert.Single(trilha);
        Assert.Equal(("gestor.a", a.Id, 1), (acesso.Login, acesso.EmpresaId, acesso.Linhas));
        Assert.StartsWith("exportar csv de ", acesso.OQue);
    }

    [Fact]
    public async Task Exportacao_em_laco_e_429()
    {
        var admin = await _api.AdminAsync();
        var respostas = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
            respostas.Add((await admin.GetAsync("/api/dashboard/exportar")).StatusCode);

        Assert.All(respostas.Take(10), s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, respostas[^1]);
    }

    [Fact]
    public async Task Registro_com_codigo_desconhecido_ou_sem_assento_deixa_a_maquina_anonima_e_diz_por_que()
    {
        var admin = await _api.AdminAsync();
        var cheia = await ApiDeTeste.EmpresaAsync(admin, "Cheia", assentos: 1);
        await _api.MaquinaAsync("maq-1", cheia.CodigoDeAdesao, "Ana");

        var semAssento = await Registrar("maq-2", cheia.CodigoDeAdesao);
        var desconhecido = await Registrar("maq-3", "DK-NAO-EXISTE");
        var vinculada = await Registrar("maq-1", cheia.CodigoDeAdesao); // a mesma máquina registrando de novo não ocupa outro assento

        Assert.Null(semAssento.Empresa);
        Assert.Contains("não tem assento livre", semAssento.Adesao);
        Assert.Null(desconhecido.Empresa);
        Assert.Contains("não reconhecido", desconhecido.Adesao);
        Assert.Equal("Cheia", vinculada.Empresa);
        // Um colaborador só (o assento é um), com o nome que ele informou por último.
        Assert.Equal(new[] { "Alguém" }, (await admin.GetFromJsonAsync<ColaboradorResumo[]>("/api/dashboard/colaboradores"))!.Select(c => c.Colaborador));
    }

    private async Task<MachineRegistrationResponseV1> Registrar(string maquinaId, string codigoEmpresa)
    {
        var resposta = await _api.CreateClient().PostAsJsonAsync("/api/maquinas/registrar",
            new MachineRegistrationV1(maquinaId, "1.4.0", ApiDeTeste.CodigoDeRegistro, codigoEmpresa, "Alguém"));
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<MachineRegistrationResponseV1>())!;
    }
}
