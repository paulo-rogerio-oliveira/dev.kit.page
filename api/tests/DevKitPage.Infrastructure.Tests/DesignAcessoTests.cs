using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O ACESSO aos projetos de design na infraestrutura: o script do Azure SQL com a tabela e a coluna do dono, e a
/// migração que reconhece o dono dos projetos que já existiam e deixa editor quem já tinha enviado versões.
/// </summary>
public sealed class DesignAcessoTests
{
    private const string AntesDoAcesso = "20261031230000_UltimoLoginDoUsuario";

    [Fact]
    public void Script_do_azure_sql_cria_os_acessos_e_o_dono_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("DesignAcesso.sql");

        Assert.Contains("OBJECT_ID(N'[dbo].[AcessosAoDesign]', N'U') IS NULL", script);
        var compartilhado = BaseDeTeste.ScriptDoAzureSql("AcessoAosProjetos.sql");
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("AcessosAosProjetos"))
            Assert.Contains(coluna, compartilhado);
        Assert.Contains("CREATE UNIQUE INDEX [IX_AcessosAoDesign_ProjetoId_UsuarioId]", script);
        Assert.Contains("COL_LENGTH(N'dbo.ProjetosDeDesign', N'DonoId') IS NULL", script);
        Assert.Contains(BaseDeTeste.ColunasNoSqlServer("ProjetosDeDesign").Single(c => c.StartsWith("[DonoId]", StringComparison.Ordinal)), script);
        Assert.Equal(["DesignAcesso.sql"], EsquemaDoBanco.ScriptsQueTrazem("ProjetosDeDesign", "DonoId"));
    }

    [Fact]
    public async Task A_migracao_cria_os_acessos_e_nao_deixa_mudanca_de_modelo_pendente()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();

        var (aplicadas, pendente) = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            return ((await db.Database.GetAppliedMigrationsAsync()).ToArray(), db.Database.HasPendingModelChanges());
        });

        Assert.Contains(aplicadas, m => m.EndsWith("_DesignAcesso", StringComparison.Ordinal));
        Assert.False(pendente);
    }

    [Fact]
    public async Task A_migracao_reconhece_o_dono_e_deixa_editor_quem_ja_enviou_versoes()
    {
        // Uma base NOVA migrada só até antes do acesso (o SQLite não desfaz o AddColumn: não há Down a usar). A empresa e
        // os usuários entram pelo modelo — as tabelas deles não mudam —; o design, pelo SQL do esquema antigo.
        await using var baseDeTeste = new BaseDeTeste();
        var (empresa, ana, bia, caio) = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            await db.GetService<IMigrator>().MigrateAsync(AntesDoAcesso);
            var acme = new Empresa { Nome = "Acme", Plano = "Empresarial", Assentos = 10, CodigoDeAdesao = "ACME-1", CriadaEmUtc = DateTime.UtcNow };
            db.Empresas.Add(acme);
            await db.SaveChangesAsync();
            Usuario Dev(string login) => new() { Login = login, Papel = "dev", EmpresaId = acme.Id, CriadoEmUtc = DateTime.UtcNow };
            var (a, b, c) = (Dev("ana"), Dev("bia"), Dev("caio"));
            db.Usuarios.AddRange(a, b, c);
            await db.SaveChangesAsync();
            return (acme.Id, a.Id, b.Id, c.Id);
        });

        await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            // O projeto da empresa: a Bia enviou a primeira versão, e a Ana e a Bia enviaram outras. O da Caio é do nível usuário.
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO ProjetosDeDesign (Id, EmpresaId, Espaco, Nivel, Time, UsuarioId, Repositorio, Chave, CriadoEmUtc, AtualizadoEmUtc)
                VALUES (1, {empresa}, 'empresa:1', 'empresa', '', 0, 'https://github.com/acme/portal', 'github.com/acme/portal', '2026-10-24 10:00:00', '2026-10-24 10:00:00'),
                       (2, {empresa}, 'empresa:1', 'usuario', '', {caio}, 'https://github.com/acme/app', 'github.com/acme/app', '2026-10-24 10:00:00', '2026-10-24 10:00:00'),
                       (3, {empresa}, 'empresa:1', 'empresa', '', 0, 'https://github.com/acme/vazio', 'github.com/acme/vazio', '2026-10-24 10:00:00', '2026-10-24 10:00:00')
                """);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO TelasDeDesign (Id, ProjetoId, Identificador, Nome, Estado) VALUES (1, 1, 'home', 'Home', ''), (2, 2, 'login', 'Login', '')");
            foreach (var (id, tela, numero, autor, enviada) in new[] { (1, 1, 1, bia, "2026-10-24 10:00:00"), (2, 1, 2, ana, "2026-10-25 10:00:00"), (3, 1, 3, bia, "2026-10-26 10:00:00"), (4, 2, 1, caio, "2026-10-24 10:00:00") })
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO VersoesDeDesign (Id, TelaId, Numero, Tipo, Tema, Largura, Altura, Origem, Alvo, Branch, "Commit", Descricao, AutorId,
                        AutorTipo, Runner, CriadaEmUtc, EnviadaEmUtc, Sha256, Bytes, ChaveDaImagem)
                    VALUES ({id}, {tela}, {numero}, 'captura', 'light', 10, 10, 'web', '', 'main', 'abc', '', {autor}, 'agente', '', {enviada}, {enviada}, {"sha" + id}, 10, {"k" + id})
                    """);

            await db.GetService<IMigrator>().MigrateAsync();
            return 0;
        });

        var projetos = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().ProjetosDeDesign.AsNoTracking()
            .Include(p => p.Acessos).OrderBy(p => p.Id).ToListAsync());

        // O dono reconhecido: quem enviou a primeira versão; quem enviou depois fica editor, sem repetir o dono.
        Assert.Equal(bia, projetos[0].DonoId);
        Assert.Equal(new[] { (ana, "editor") }, projetos[0].Acessos.Select(a => (a.UsuarioId, a.Papel)));
        // O nível usuário é do dono dele; o projeto sem versão fica sem dono (o próximo envio o assume).
        Assert.Equal((caio, 0), (projetos[1].DonoId, projetos[1].Acessos.Count));
        Assert.Equal((0, 0), (projetos[2].DonoId, projetos[2].Acessos.Count));

        // Pelo serviço, a Ana (editora) continua vendo e enviando; a Caio não vê o da empresa.
        var daAna = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IDesignDaEmpresa>().ProjetosAsync(new EscopoDoDesign(ana, empresa, false), null, null, default));
        Assert.Equal(("github.com/acme/portal", "editor", "bia"), (Assert.Single(daAna).Chave, daAna[0].Papel, daAna[0].Dono));
        var daCaio = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IDesignDaEmpresa>().ProjetosAsync(new EscopoDoDesign(caio, empresa, false), null, null, default));
        Assert.Equal("github.com/acme/app", Assert.Single(daCaio).Chave);
    }
}
