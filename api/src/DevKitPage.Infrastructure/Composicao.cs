using System.Security.Cryptography;
using DevKitPage.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>O registro da infraestrutura: o provider do banco sai da configuração, não do código.</summary>
public static class Composicao
{
    /// <summary>A connection string padrão: o arquivo SQLite ao lado da API.</summary>
    public const string ConexaoPadrao = "Data Source=dados/devkitpage.db";

    public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection servicos, IConfiguration configuracao)
    {
        servicos.Configure<OpcoesDeAutenticacao>(configuracao.GetSection(OpcoesDeAutenticacao.Secao));
        servicos.Configure<OpcoesDeTelemetria>(configuracao.GetSection(OpcoesDeTelemetria.Secao));
        servicos.Configure<OpcoesDoBanco>(configuracao.GetSection(OpcoesDoBanco.Secao));
        servicos.Configure<OpcoesDaSemente>(configuracao.GetSection(OpcoesDaSemente.Secao));
        servicos.Configure<OpcoesDeDemonstracao>(configuracao.GetSection(OpcoesDeDemonstracao.Secao));

        // Lidos na criação do contexto, e não aqui: a configuração final (variáveis de ambiente, Key
        // Vault, a dos testes) só está completa depois do Build do host.
        servicos.AddDbContext<DevKitPageDb>((provedor, opcoes) =>
        {
            var cfg = provedor.GetRequiredService<IConfiguration>();
            var provider = provedor.GetRequiredService<IOptions<OpcoesDoBanco>>().Value.Provider;
            var conexao = cfg.GetConnectionString("DevKitPage") ?? ConexaoPadrao;
            if (string.Equals(provider, OpcoesDoBanco.SqlServer, StringComparison.OrdinalIgnoreCase))
                opcoes.UseSqlServer(conexao);
            else
                opcoes.UseSqlite(conexao);
        });

        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
        servicos.AddScoped<IUsuarios, Usuarios>();
        servicos.AddScoped<IMaquinas, Maquinas>();
        servicos.AddScoped<IIngestaoDeTelemetria, IngestaoDeTelemetria>();
        servicos.AddScoped<IConsultasDoPainel, ConsultasDoPainel>();
        servicos.AddScoped<IExpurgoDeEventos, ExpurgoDeEventos>();
        servicos.AddScoped<IReacaoAErros, ReacaoAErros>();
        servicos.AddScoped<IExpurgoDeOcorrencias, ExpurgoDeOcorrencias>();
        servicos.AddScoped<IEmpresas, Empresas>();
        servicos.AddScoped<IAuditoriaDeAcesso, AuditoriaDeAcesso>();
        servicos.AddScoped<IExpurgoDeAcessos, ExpurgoDeAcessos>();
        servicos.AddScoped<IPedidosDeDemonstracao, PedidosDeDemonstracao>();
        servicos.AddScoped<IExpurgoDePedidos, ExpurgoDePedidos>();
        servicos.AddScoped<InicializadorDaBase>();
        return servicos;
    }
}

/// <summary>
/// A primeira (e cada) subida: aplica as migrations e semeia o admin UMA vez. A segunda subida não
/// altera nada — a semente só roda com a tabela de usuários vazia.
/// </summary>
public sealed class InicializadorDaBase(
    DevKitPageDb db, IPasswordHasher<Usuario> hasher, IOptions<OpcoesDaSemente> semente, TimeProvider relogio,
    ILogger<InicializadorDaBase> log)
{
    public async Task InicializarAsync(CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
        {
            CriarPastaDoArquivo(db.Database.GetConnectionString());
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            // As migrations versionadas são as do SQLite (a base embarcada). Num provider trocado por
            // configuração (Azure SQL), o esquema nasce do modelo — ver docs/arquitetura.md. Numa base
            // que JÁ existe o EnsureCreated não cria tabela nova: ela vem dos scripts de api/scripts/sqlserver.
            await db.Database.EnsureCreatedAsync(ct);
        }

        await SemearAdminAsync(ct);
    }

    private async Task SemearAdminAsync(CancellationToken ct)
    {
        if (await db.Usuarios.AnyAsync(ct))
            return;

        var login = string.IsNullOrWhiteSpace(semente.Value.AdminLogin) ? "admin" : semente.Value.AdminLogin.Trim();
        var senha = semente.Value.AdminPassword;
        var gerada = string.IsNullOrWhiteSpace(senha);
        if (gerada)
            senha = SenhaAleatoria();

        var admin = new Usuario
        {
            Login = login,
            EhAdmin = true,
            Papel = Contracts.V1.Papeis.Admin,
            DeveTrocarSenha = true,
            CriadoEmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        admin.SenhaHash = hasher.HashPassword(admin, senha);
        db.Usuarios.Add(admin);
        await db.SaveChangesAsync(ct);

        if (gerada)
        {
            // UMA vez, na subida que criou o admin: é a única forma de saber a senha sem configurá-la.
            log.LogWarning("Usuário '{Login}' criado com a senha inicial gerada: {Senha} — troque-a no primeiro acesso.", login, senha);
        }
        else
        {
            log.LogInformation("Usuário '{Login}' criado com a senha de Seed:AdminPassword — troca obrigatória no primeiro acesso.", login);
        }
    }

    /// <summary>Uma senha que passa na <see cref="PoliticaDeSenha"/>.</summary>
    public static string SenhaAleatoria()
        => "Dk" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) + "a1";

    private static void CriarPastaDoArquivo(string? conexao)
    {
        if (string.IsNullOrWhiteSpace(conexao))
            return;
        var arquivo = new SqliteConnectionStringBuilder(conexao).DataSource;
        if (string.IsNullOrWhiteSpace(arquivo) || arquivo == ":memory:" || arquivo.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return;
        var pasta = Path.GetDirectoryName(Path.GetFullPath(arquivo));
        if (!string.IsNullOrEmpty(pasta))
            Directory.CreateDirectory(pasta);
    }
}
