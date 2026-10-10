using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A gestão de usuários pelo admin (US #405) na base de verdade: a senha temporária (uma vez, com a troca
/// obrigatória), a redefinição, o bloqueio pelo admin (diferente do temporário por falhas), o papel dev
/// sem painel e as regras que dependem do banco (login repetido, empresa inexistente, o admin sobre si).
/// </summary>
public sealed class UsuariosTests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new();

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<T> Gestao<T>(Func<IGestaoDeUsuarios, Task<T>> acao) => _base.ComAsync(sp => acao(sp.GetRequiredService<IGestaoDeUsuarios>()));

    private Task<(ResultadoDoLogin Resultado, Usuario? Usuario)> Entrar(string login, string senha)
        => _base.ComAsync(sp => sp.GetRequiredService<IUsuarios>().AutenticarAsync(login, senha, default));

    private Task<int> IdDoAdmin() => _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Login == "admin").Select(u => u.Id).SingleAsync());

    [Fact]
    public async Task Criar_gera_senha_temporaria_que_entra_uma_vez_com_a_troca_obrigatoria()
    {
        var criado = (await Gestao(g => g.CriarAsync(new UsuarioNovo(" ana.dev ", " Ana Souza ", Papeis.Dev, null), default))).Valor!;

        Assert.Equal(("ana.dev", "Ana Souza", Papeis.Dev, true, false), (criado.Usuario.Login, criado.Usuario.Nome, criado.Usuario.Papel, criado.Usuario.DeveTrocarSenha, criado.Usuario.Bloqueado));
        Assert.Empty(PoliticaDeSenha.Validar(criado.SenhaTemporaria));
        // Só o hash vai para o banco, e a lista não traz senha nenhuma.
        var gravado = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.AsNoTracking().SingleAsync(u => u.Login == "ana.dev"));
        Assert.DoesNotContain(criado.SenhaTemporaria, gravado.SenhaHash);
        Assert.False(gravado.EhAdmin);

        var (resultado, usuario) = await Entrar("ana.dev", criado.SenhaTemporaria);
        Assert.Equal(ResultadoDoLogin.Ok, resultado);
        Assert.True(usuario!.DeveTrocarSenha);
        Assert.Empty(await _base.ComAsync(sp => sp.GetRequiredService<IUsuarios>().TrocarSenhaAsync(usuario.Id, criado.SenhaTemporaria, "senhaDaAna2026", default)));
        Assert.Equal(ResultadoDoLogin.CredencialInvalida, (await Entrar("ana.dev", criado.SenhaTemporaria)).Resultado);
    }

    [Fact]
    public async Task Papel_dev_entra_no_app_mas_nao_tem_escopo_no_painel()
    {
        var empresa = await _base.ComAsync(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova("Empresa A", "Empresarial", 5), default));
        var criado = (await Gestao(g => g.CriarAsync(new UsuarioNovo("ana.dev", "Ana", Papeis.Dev, empresa.Id), default))).Valor!;

        var (resultado, usuario) = await Entrar("ana.dev", criado.SenhaTemporaria);

        Assert.Equal(ResultadoDoLogin.Ok, resultado);
        Assert.Equal((Papeis.Dev, (int?)empresa.Id), (usuario!.Papel, usuario.EmpresaId));
        Assert.Equal("Empresa A", criado.Usuario.Empresa);
        // O escopo do painel do dev é nulo — com ou sem empresa: a rota responde 403.
        Assert.Null(EscopoDoPainel.DasClaims(usuario.Papel, empresa.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.False(Papeis.EntraNoPainel(usuario.Papel));
    }

    [Fact]
    public async Task Redefinir_troca_a_senha_exige_a_troca_e_tira_o_bloqueio_temporario()
    {
        var criado = (await Gestao(g => g.CriarAsync(new UsuarioNovo("ana.dev", "Ana", Papeis.Dev, null), default))).Valor!;
        for (var i = 0; i < 5; i++)
            await Entrar("ana.dev", "errada");
        Assert.NotNull((await Gestao(g => g.ListarAsync(default))).Single(u => u.Login == "ana.dev").BloqueadoAte);

        var redefinido = (await Gestao(g => g.RedefinirSenhaAsync(criado.Usuario.Id, default))).Valor!;

        Assert.NotEqual(criado.SenhaTemporaria, redefinido.SenhaTemporaria);
        Assert.True(redefinido.Usuario.DeveTrocarSenha);
        Assert.Null(redefinido.Usuario.BloqueadoAte);
        Assert.Equal(ResultadoDoLogin.CredencialInvalida, (await Entrar("ana.dev", criado.SenhaTemporaria)).Resultado);
        Assert.Equal(ResultadoDoLogin.Ok, (await Entrar("ana.dev", redefinido.SenhaTemporaria)).Resultado);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Gestao(g => g.RedefinirSenhaAsync(999, default))).Falha);
    }

    [Fact]
    public async Task Bloqueio_do_admin_so_aparece_com_a_senha_certa_e_so_sai_ao_desbloquear()
    {
        var criado = (await Gestao(g => g.CriarAsync(new UsuarioNovo("ana.dev", "Ana", Papeis.Dev, null), default))).Valor!;
        var admin = await IdDoAdmin();

        var bloqueado = (await Gestao(g => g.BloquearAsync(criado.Usuario.Id, true, admin, default))).Valor!;
        _base.Relogio.Agora = _base.Relogio.Agora.AddDays(30); // não vence sozinho

        Assert.True(bloqueado.Bloqueado);
        Assert.Equal(ResultadoDoLogin.BloqueadoPeloAdmin, (await Entrar("ana.dev", criado.SenhaTemporaria)).Resultado);
        Assert.Equal(ResultadoDoLogin.CredencialInvalida, (await Entrar("ana.dev", "errada")).Resultado); // sem a senha, nada revela o bloqueio

        var desbloqueado = (await Gestao(g => g.BloquearAsync(criado.Usuario.Id, false, admin, default))).Valor!;
        Assert.False(desbloqueado.Bloqueado);
        Assert.Equal(ResultadoDoLogin.Ok, (await Entrar("ana.dev", criado.SenhaTemporaria)).Resultado);
    }

    [Fact]
    public async Task Bloqueio_temporario_devolve_o_usuario_com_o_instante_de_tentar_de_novo()
    {
        for (var i = 0; i < 5; i++)
            await Entrar("admin", "errada");

        var (resultado, usuario) = await Entrar("admin", "senhaInicial2026");

        Assert.Equal(ResultadoDoLogin.Bloqueado, resultado);
        Assert.Equal(_base.Relogio.Agora.UtcDateTime.AddMinutes(15), usuario!.BloqueadoAteUtc);
    }

    [Fact]
    public async Task Editar_muda_nome_papel_e_empresa_e_o_admin_nao_mexe_no_proprio_papel_nem_se_bloqueia()
    {
        var empresa = await _base.ComAsync(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova("Empresa A", "Empresarial", 5), default));
        var criado = (await Gestao(g => g.CriarAsync(new UsuarioNovo("ana", "Ana", Papeis.Dev, null), default))).Valor!;
        var admin = await IdDoAdmin();

        var gestora = (await Gestao(g => g.EditarAsync(criado.Usuario.Id, new UsuarioEditado("Ana Souza", Papeis.Gestor, empresa.Id), admin, default))).Valor!;
        var promovida = (await Gestao(g => g.EditarAsync(criado.Usuario.Id, new UsuarioEditado("Ana Souza", Papeis.Admin, null), admin, default))).Valor!;
        var semEmpresa = await Gestao(g => g.EditarAsync(criado.Usuario.Id, new UsuarioEditado("Ana", Papeis.Dev, 999), admin, default));
        var rebaixarASi = await Gestao(g => g.EditarAsync(admin, new UsuarioEditado("Admin", Papeis.Dev, null), admin, default));
        var bloquearASi = await Gestao(g => g.BloquearAsync(admin, true, admin, default));
        var renomearASi = await Gestao(g => g.EditarAsync(admin, new UsuarioEditado("Administrador", Papeis.Admin, null), admin, default));

        Assert.Equal(("Ana Souza", Papeis.Gestor, "Empresa A"), (gestora.Nome, gestora.Papel, gestora.Empresa));
        Assert.Equal((Papeis.Admin, (int?)null), (promovida.Papel, promovida.EmpresaId));
        Assert.True(await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Id == criado.Usuario.Id).Select(u => u.EhAdmin).SingleAsync()));
        Assert.Equal(FalhaNaGestao.Recusada, semEmpresa.Falha);
        Assert.Equal(FalhaNaGestao.Recusada, rebaixarASi.Falha);
        Assert.Equal(FalhaNaGestao.Recusada, bloquearASi.Falha);
        Assert.Equal("Administrador", renomearASi.Valor!.Nome);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Gestao(g => g.EditarAsync(999, new UsuarioEditado("x", Papeis.Dev, null), admin, default))).Falha);
    }

    [Fact]
    public async Task Login_repetido_e_conflito_e_empresa_inexistente_e_recusada()
    {
        await Gestao(g => g.CriarAsync(new UsuarioNovo("ana", "Ana", Papeis.Dev, null), default));

        var repetido = await Gestao(g => g.CriarAsync(new UsuarioNovo("ana", "Outra", Papeis.Dev, null), default));
        var admin = await Gestao(g => g.CriarAsync(new UsuarioNovo("admin", "", Papeis.Dev, null), default));
        var semEmpresa = await Gestao(g => g.CriarAsync(new UsuarioNovo("bia", "", Papeis.Gestor, 999), default));

        Assert.Equal((FalhaNaGestao.Conflito, FalhaNaGestao.Conflito, FalhaNaGestao.Recusada), (repetido.Falha, admin.Falha, semEmpresa.Falha));
        Assert.Null(repetido.Valor);
        Assert.Equal(new[] { "admin", "ana" }, (await Gestao(g => g.ListarAsync(default))).Select(u => u.Login));
    }

    [Fact]
    public async Task Convite_de_gestor_usa_a_mesma_criacao_e_o_mesmo_gerador_de_senha()
    {
        var empresa = await _base.ComAsync(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova("Empresa A", "Empresarial", 5), default));

        var (gestor, erro) = await _base.ComAsync(sp => sp.GetRequiredService<IEmpresas>().CriarGestorAsync(empresa.Id, " gestor.a ", default));
        var (_, repetido) = await _base.ComAsync(sp => sp.GetRequiredService<IEmpresas>().CriarGestorAsync(empresa.Id, "gestor.a", default));

        Assert.Empty(erro);
        Assert.Equal("gestor.a", gestor!.Login);
        Assert.Empty(PoliticaDeSenha.Validar(gestor.SenhaInicial));
        Assert.Contains("Já existe", repetido);
        var gravado = (await Gestao(g => g.ListarAsync(default))).Single(u => u.Login == "gestor.a");
        Assert.Equal((Papeis.Gestor, (int?)empresa.Id, true), (gravado.Papel, gravado.EmpresaId, gravado.DeveTrocarSenha));
        var hasher = _base.Servicos.GetRequiredService<IPasswordHasher<Usuario>>();
        var usuario = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.AsNoTracking().SingleAsync(u => u.Login == "gestor.a"));
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, gestor.SenhaInicial));
    }

    [Fact]
    public void Script_do_azure_sql_acrescenta_o_nome_e_o_bloqueio_com_o_tipo_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Usuarios.sql");
        var usuarios = BaseDeTeste.ColunasNoSqlServer("Usuarios");

        foreach (var coluna in new[] { "[Nome]", "[BloqueadoPeloAdmin]" })
        {
            Assert.Contains($"COL_LENGTH(N'dbo.Usuarios', N'{coluna.Trim('[', ']')}') IS NULL", script);
            Assert.Contains(usuarios.Single(c => c.StartsWith(coluna, StringComparison.Ordinal)).Replace(" NOT NULL", string.Empty, StringComparison.Ordinal), script);
        }
    }
}
