using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A gestão de usuários pelo admin (US #405, #407) de ponta a ponta: só o admin administra; a senha
/// temporária sai uma vez (criação e redefinição); o dev entra no app e recebe 403 no painel; e o 423 do
/// login diz até quando o bloqueio temporário vale.
/// </summary>
public sealed class UsuariosApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Anonimo_e_401_e_quem_nao_e_admin_e_403()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var gestor = await _api.GestorAsync(admin, empresa.Id, "gestor.a");
        var (dev, _) = await _api.UsuarioAsync(admin, "ana.dev", Papeis.Dev);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/usuarios")).StatusCode);
        foreach (var cliente in new[] { gestor, dev })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/usuarios")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("x", "", Papeis.Dev, null))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsync("/api/usuarios/1/senha", null)).StatusCode);
        }
    }

    [Fact]
    public async Task Senha_temporaria_sai_so_na_criacao_e_na_redefinicao()
    {
        var admin = await _api.AdminAsync();

        var criacao = await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("ana.dev", "Ana Souza", Papeis.Dev, null));
        var criado = (await criacao.Content.ReadFromJsonAsync<UsuarioComSenha>())!;
        var lista = await admin.GetStringAsync("/api/usuarios");
        var edicao = await admin.PutAsJsonAsync($"/api/usuarios/{criado.Usuario.Id}", new UsuarioEditado("Ana S.", Papeis.Dev, null));
        var textoDaEdicao = await edicao.Content.ReadAsStringAsync();
        var redefinicao = await admin.PostAsync($"/api/usuarios/{criado.Usuario.Id}/senha", null);
        var redefinido = (await redefinicao.Content.ReadFromJsonAsync<UsuarioComSenha>())!;

        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        Assert.Equal($"/api/usuarios/{criado.Usuario.Id}", criacao.Headers.Location!.OriginalString);
        Assert.False(string.IsNullOrWhiteSpace(criado.SenhaTemporaria));
        // A lista e a edição não trazem senha nenhuma — nem a temporária, nem o hash.
        Assert.DoesNotContain(criado.SenhaTemporaria, lista);
        Assert.DoesNotContain("senhaTemporaria", lista, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("senhaHash", lista, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, edicao.StatusCode);
        Assert.DoesNotContain("senhaTemporaria", textoDaEdicao, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(criado.SenhaTemporaria, textoDaEdicao);
        Assert.Equal("Ana S.", JsonSerializer.Deserialize<UsuarioResumo>(textoDaEdicao, JsonSerializerOptions.Web)!.Nome);
        Assert.Equal(HttpStatusCode.OK, redefinicao.StatusCode);
        Assert.NotEqual(criado.SenhaTemporaria, redefinido.SenhaTemporaria);

        // A temporária redefinida entra, com a troca obrigatória; a primeira não entra mais.
        var velha = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("ana.dev", criado.SenhaTemporaria));
        var nova = (await (await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("ana.dev", redefinido.SenhaTemporaria))).Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, velha.StatusCode);
        Assert.Equal((true, Papeis.Dev, false), (nova.DeveTrocarSenha, nova.Papel, nova.EhAdmin));
    }

    [Fact]
    public async Task Validacao_conflito_e_inexistente()
    {
        var admin = await _api.AdminAsync();

        var invalido = await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("com espaço", "", "visitante", null));
        var gestorSemEmpresa = await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("gestor", "", Papeis.Gestor, null));
        var repetido = await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("admin", "", Papeis.Dev, null));
        var empresaInexistente = await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("bia", "", Papeis.Dev, 999));
        var editarInexistente = await admin.PutAsJsonAsync("/api/usuarios/999", new UsuarioEditado("x", Papeis.Dev, null));
        var bloquearInexistente = await admin.PutAsJsonAsync("/api/usuarios/999/bloqueio", new BloqueioDoUsuario(true));

        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Contains("\"login\"", await invalido.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, gestorSemEmpresa.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empresaInexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, editarInexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bloquearInexistente.StatusCode);
    }

    [Fact]
    public async Task Admin_nao_se_bloqueia_nem_muda_o_proprio_papel()
    {
        var admin = await _api.AdminAsync();
        var eu = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == "admin");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/usuarios/{eu.Id}/bloqueio", new BloqueioDoUsuario(true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/usuarios/{eu.Id}", new UsuarioEditado("Eu", Papeis.Dev, null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/usuarios/{eu.Id}", new UsuarioEditado("Administrador", Papeis.Admin, null))).StatusCode);
    }

    [Fact]
    public async Task Dev_entra_no_app_mas_o_painel_inteiro_e_403()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (dev, login) = await _api.UsuarioAsync(admin, "ana.dev", Papeis.Dev, empresa.Id);

        Assert.Equal((Papeis.Dev, false, false), (login.Papel, login.EhAdmin, login.DeveTrocarSenha));
        foreach (var rota in new[] { "/api/dashboard/maquinas", "/api/dashboard/quantidade", "/api/dashboard/qualidade", "/api/dashboard/eventos",
                     "/api/dashboard/erros", "/api/dashboard/roi", "/api/dashboard/colaboradores", "/api/dashboard/exportar", "/api/dashboard/demonstracoes", "/api/empresas" })
            Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dev.GetAsync("/api/auth/politica")).StatusCode);
    }

    [Fact]
    public async Task Login_bloqueado_por_falhas_devolve_423_com_bloqueado_ate()
    {
        var cliente = _api.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "errada"))).StatusCode);

        var antes = DateTimeOffset.UtcNow;
        var bloqueado = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", ApiDeTeste.SenhaInicial));
        using var problema = JsonDocument.Parse(await bloqueado.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)423, bloqueado.StatusCode);
        var ate = problema.RootElement.GetProperty("bloqueadoAte").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, ate.Offset); // ISO em UTC
        Assert.InRange(ate, antes.AddMinutes(14), antes.AddMinutes(16));
    }

    [Fact]
    public async Task Login_bloqueado_pelo_admin_e_423_sem_bloqueado_ate_e_desbloquear_devolve_o_acesso()
    {
        var admin = await _api.AdminAsync();
        var criado = (await (await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("ana.dev", "", Papeis.Dev, null))).Content.ReadFromJsonAsync<UsuarioComSenha>())!;
        var bloqueio = await admin.PutAsJsonAsync($"/api/usuarios/{criado.Usuario.Id}/bloqueio", new BloqueioDoUsuario(true));

        var bloqueado = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("ana.dev", criado.SenhaTemporaria));
        using var problema = JsonDocument.Parse(await bloqueado.Content.ReadAsStringAsync());
        var semSenha = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("ana.dev", "errada"));
        (await admin.PutAsJsonAsync($"/api/usuarios/{criado.Usuario.Id}/bloqueio", new BloqueioDoUsuario(false))).EnsureSuccessStatusCode();
        var liberado = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("ana.dev", criado.SenhaTemporaria));

        Assert.True((await bloqueio.Content.ReadFromJsonAsync<UsuarioResumo>())!.Bloqueado);
        Assert.Equal((HttpStatusCode)423, bloqueado.StatusCode);
        Assert.False(problema.RootElement.TryGetProperty("bloqueadoAte", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, semSenha.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liberado.StatusCode);
    }

    [Fact]
    public async Task Admin_cria_usuario_e_o_usuario_troca_a_senha_temporaria_no_primeiro_acesso()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");

        var (gestor, login) = await _api.UsuarioAsync(admin, "gestor.b", Papeis.Gestor, empresa.Id);

        Assert.Equal((Papeis.Gestor, "Empresa A", false), (login.Papel, login.Empresa, login.DeveTrocarSenha));
        Assert.Equal(HttpStatusCode.OK, (await gestor.GetAsync("/api/dashboard/quantidade")).StatusCode);
        var listado = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == "gestor.b");
        Assert.Equal(("Nome de gestor.b", false, (int?)empresa.Id), (listado.Nome, listado.DeveTrocarSenha, listado.EmpresaId));
    }
}
