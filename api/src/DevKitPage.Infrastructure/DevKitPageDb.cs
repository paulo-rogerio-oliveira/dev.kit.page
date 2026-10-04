using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A base do dev.kit.page: usuários, máquinas, eventos brutos, totais diários e pedidos de demonstração. As datas vão em
/// UTC (<see cref="DateTime"/>) — o SQLite não compara <see cref="DateTimeOffset"/> no SQL, e a
/// API converte na borda.
/// </summary>
public sealed class DevKitPageDb(DbContextOptions<DevKitPageDb> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Maquina> Maquinas => Set<Maquina>();
    public DbSet<EventoDeUso> Eventos => Set<EventoDeUso>();
    public DbSet<TotalDiario> TotaisDiarios => Set<TotalDiario>();
    public DbSet<PedidoDeDemonstracao> PedidosDeDemonstracao => Set<PedidoDeDemonstracao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Usuario>(e =>
        {
            e.ToTable("Usuarios");
            e.Property(u => u.Login).HasMaxLength(100);
            e.Property(u => u.SenhaHash).HasMaxLength(500);
            e.HasIndex(u => u.Login).IsUnique();
        });

        modelBuilder.Entity<Maquina>(e =>
        {
            e.ToTable("Maquinas");
            e.Property(m => m.MaquinaId).HasMaxLength(64);
            e.Property(m => m.Apelido).HasMaxLength(100);
            e.Property(m => m.ChaveHash).HasMaxLength(64);
            e.Property(m => m.VersaoDevKit).HasMaxLength(50);
            e.HasIndex(m => m.MaquinaId).IsUnique();
            e.HasIndex(m => m.ChaveHash).IsUnique();
        });

        modelBuilder.Entity<EventoDeUso>(e =>
        {
            e.ToTable("EventosDeUso");
            e.Property(x => x.EventId).HasMaxLength(64);
            e.Property(x => x.Tipo).HasMaxLength(50);
            e.Property(x => x.SessaoId).HasMaxLength(100);
            e.Property(x => x.Detalhe).HasMaxLength(ValidadorDeLote.TamanhoMaximoDoTexto);
            // O eventId ÚNICO é o que torna o reenvio do dev.kit inofensivo.
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => new { x.MaquinaId, x.EmUtc });
            e.HasIndex(x => x.EmUtc);
            e.HasOne(x => x.Maquina).WithMany().HasForeignKey(x => x.MaquinaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TotalDiario>(e =>
        {
            e.ToTable("TotaisDiarios");
            e.Property(x => x.Tipo).HasMaxLength(50);
            e.Property(x => x.Detalhe).HasMaxLength(ValidadorDeLote.TamanhoMaximoDoTexto);
            e.HasIndex(x => new { x.MaquinaId, x.Dia, x.Tipo, x.Detalhe }).IsUnique();
            e.HasIndex(x => x.Dia);
            e.HasOne<Maquina>().WithMany().HasForeignKey(x => x.MaquinaId).OnDelete(DeleteBehavior.Cascade);
        });

        // Sem relação com a telemetria. No Azure SQL a tabela nasce do script
        // api/scripts/sqlserver/PedidosDeDemonstracao.sql (o EnsureCreated não cria tabela em base existente).
        modelBuilder.Entity<PedidoDeDemonstracao>(e =>
        {
            e.ToTable("PedidosDeDemonstracao");
            e.Property(p => p.Nome).HasMaxLength(ValidadorDeDemonstracao.TamanhoMaximoDoNome);
            e.Property(p => p.Email).HasMaxLength(ValidadorDeDemonstracao.TamanhoMaximoDoEmail);
            e.Property(p => p.Empresa).HasMaxLength(ValidadorDeDemonstracao.TamanhoMaximoDaEmpresa);
            e.Property(p => p.Mensagem).HasMaxLength(ValidadorDeDemonstracao.TamanhoMaximoDaMensagem);
            e.HasIndex(p => p.RecebidoEmUtc);
        });
    }

    /// <summary>A data UTC lida do banco (o provider devolve <see cref="DateTimeKind.Unspecified"/>).</summary>
    public static DateTimeOffset Utc(DateTime data) => new(DateTime.SpecifyKind(data, DateTimeKind.Utc));
}
