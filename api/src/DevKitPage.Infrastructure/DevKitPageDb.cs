using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A base do dev.kit.page: usuários, máquinas, eventos brutos, totais diários, grupos e ocorrências de
/// exceção não classificada, fotos do ROI por work item, avaliações de entrega e pedidos de demonstração. As datas vão em
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
    public DbSet<GrupoDeErro> GruposDeErro => Set<GrupoDeErro>();
    public DbSet<OcorrenciaDeErro> OcorrenciasDeErro => Set<OcorrenciaDeErro>();
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<AcessoAosDados> AcessosAosDados => Set<AcessoAosDados>();
    public DbSet<RoiDeWorkItem> RoisDeWorkItem => Set<RoiDeWorkItem>();
    public DbSet<AvaliacaoDeEntrega> AvaliacoesDeEntrega => Set<AvaliacaoDeEntrega>();
    public DbSet<ItemDeCatalogo> ItensDeCatalogo => Set<ItemDeCatalogo>();
    public DbSet<VersaoDeItem> VersoesDeItem => Set<VersaoDeItem>();
    public DbSet<AssinaturaDeCatalogo> AssinaturasDeCatalogo => Set<AssinaturaDeCatalogo>();
    public DbSet<LinkDeSenha> LinksDeSenha => Set<LinkDeSenha>();
    public DbSet<TrialDeMaquina> TrialsDeMaquina => Set<TrialDeMaquina>();
    public DbSet<LicencaEmitida> LicencasEmitidas => Set<LicencaEmitida>();
    public DbSet<EventoDePagamento> EventosDePagamento => Set<EventoDePagamento>();
    public DbSet<Catalogo> Catalogos => Set<Catalogo>();
    public DbSet<VinculoDeCatalogo> VinculosDeCatalogo => Set<VinculoDeCatalogo>();
    public DbSet<ItemDoCatalogoEditavel> ItensEditaveis => Set<ItemDoCatalogoEditavel>();
    public DbSet<VersaoDoCatalogo> VersoesDoCatalogo => Set<VersaoDoCatalogo>();
    public DbSet<PacoteDoCatalogo> PacotesDoCatalogo => Set<PacoteDoCatalogo>();
    public DbSet<RepositorioDaEmpresa> RepositoriosDaEmpresa => Set<RepositorioDaEmpresa>();
    public DbSet<ConvencaoDaEmpresa> ConvencoesDaEmpresa => Set<ConvencaoDaEmpresa>();
    public DbSet<Relato> Relatos => Set<Relato>();
    public DbSet<ProjetoDeDesign> ProjetosDeDesign => Set<ProjetoDeDesign>();
    public DbSet<TelaDeDesign> TelasDeDesign => Set<TelaDeDesign>();
    public DbSet<VersaoDeDesign> VersoesDeDesign => Set<VersaoDeDesign>();
    public DbSet<ProjetoCompartilhado> ProjetosCompartilhados => Set<ProjetoCompartilhado>();
    public DbSet<ItemDeProjetoCompartilhado> ItensDeProjetoCompartilhado => Set<ItemDeProjetoCompartilhado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Usuario>(e =>
        {
            e.ToTable("Usuarios");
            e.Property(u => u.Login).HasMaxLength(100);
            e.Property(u => u.SenhaHash).HasMaxLength(500);
            e.Property(u => u.Papel).HasMaxLength(20);
            // A gestão de usuários (US #405). No Azure SQL as colunas vêm de api/scripts/sqlserver/Usuarios.sql.
            e.Property(u => u.Nome).HasMaxLength(ValidadorDeUsuario.TamanhoMaximoDoNome);
            // O e-mail da adesão (US #425): vazio nos usuários anteriores, então o índice não é único — a regra
            // "um e-mail, uma empresa" é do serviço (Contas). No Azure SQL a coluna vem de api/scripts/sqlserver/Us425.sql.
            e.Property(u => u.Email).HasMaxLength(ValidadorDeDemonstracao.TamanhoMaximoDoEmail);
            e.HasIndex(u => u.Email);
            e.HasIndex(u => u.Login).IsUnique();
            e.HasOne(u => u.Empresa).WithMany().HasForeignKey(u => u.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        });

        // O plano empresarial (US #381). No Azure SQL as tabelas e as colunas novas vêm do script
        // api/scripts/sqlserver/Empresas.sql.
        modelBuilder.Entity<Empresa>(e =>
        {
            e.ToTable("Empresas");
            e.Property(x => x.Nome).HasMaxLength(ValidadorDeEmpresa.TamanhoMaximoDoNome);
            e.Property(x => x.Plano).HasMaxLength(ValidadorDeEmpresa.TamanhoMaximoDoPlano);
            e.Property(x => x.CodigoDeAdesao).HasMaxLength(20);
            e.Property(x => x.AssinaturaExterna).HasMaxLength(100);
            e.HasIndex(x => x.CodigoDeAdesao).IsUnique();
            e.HasIndex(x => x.AssinaturaExterna);
        });

        // O catálogo da empresa (US #425). No Azure SQL as tabelas vêm de api/scripts/sqlserver/Us425.sql.
        modelBuilder.Entity<ItemDeCatalogo>(e =>
        {
            e.ToTable("ItensDeCatalogo");
            e.Property(i => i.Tipo).HasMaxLength(10);
            e.Property(i => i.Nome).HasMaxLength(RegrasDoCatalogoDaEmpresa.TamanhoMaximoDoNome);
            e.Property(i => i.Descricao).HasMaxLength(RegrasDoCatalogoDaEmpresa.TamanhoMaximoDaDescricao);
            e.Property(i => i.HashAtual).HasMaxLength(64);
            e.Property(i => i.Dependencias).HasMaxLength(4000);
            e.HasIndex(i => new { i.EmpresaId, i.Tipo, i.Nome }).IsUnique();
            e.HasOne(i => i.Empresa).WithMany().HasForeignKey(i => i.EmpresaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Autor).WithMany().HasForeignKey(i => i.AutorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VersaoDeItem>(e =>
        {
            e.ToTable("VersoesDeItem");
            e.Property(v => v.Hash).HasMaxLength(64);
            // Até 256 KB de UTF-8 (a regra do Core): no SQL Server é nvarchar(max), como o EF gera sem tamanho.
            e.HasIndex(v => new { v.ItemId, v.Versao }).IsUnique();
            e.HasOne(v => v.Item).WithMany().HasForeignKey(v => v.ItemId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssinaturaDeCatalogo>(e =>
        {
            e.ToTable("AssinaturasDeCatalogo");
            e.HasIndex(a => new { a.ItemId, a.UsuarioId }).IsUnique();
            e.HasIndex(a => a.UsuarioId);
            e.HasOne(a => a.Item).WithMany().HasForeignKey(a => a.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Usuario>().WithMany().HasForeignKey(a => a.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        // O catálogo de repositórios e as convenções da empresa (US #491). No Azure SQL as tabelas vêm de
        // api/scripts/sqlserver/Us491.sql.
        modelBuilder.Entity<RepositorioDaEmpresa>(e =>
        {
            e.ToTable("RepositoriosDaEmpresa");
            e.Property(r => r.Origem).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDaOrigem);
            e.Property(r => r.Chave).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDaOrigem);
            e.Property(r => r.Descricao).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDaDescricao);
            e.HasIndex(r => new { r.EmpresaId, r.Chave }).IsUnique();
            e.HasOne(r => r.Empresa).WithMany().HasForeignKey(r => r.EmpresaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Autor).WithMany().HasForeignKey(r => r.AutorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConvencaoDaEmpresa>(e =>
        {
            e.ToTable("ConvencoesDaEmpresa");
            e.Property(c => c.Nivel).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoNivel);
            e.Property(c => c.Time).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoTime);
            e.Property(c => c.Chave).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDaChave);
            // O maior teto, o do contexto (US #550): o das outras convenções é da regra. No Azure SQL a coluna cresce
            // por api/scripts/sqlserver/Us550.sql.
            e.Property(c => c.Valor).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoValorDeContexto);
            // A origem (US #663): a regra do usuário e a sugestão do agente do mesmo tema são duas linhas. No Azure SQL as
            // colunas e o índice vêm de api/scripts/sqlserver/Us663.sql.
            e.Property(c => c.Origem).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDaOrigemDaConvencao).HasDefaultValue(Contracts.V1.OrigensDeConvencao.Usuario);
            e.Property(c => c.Versao).HasDefaultValue(1);
            // O UsuarioId é o dono da convenção de nível usuário e vale 0 nos outros níveis: sem FK, de propósito.
            e.HasIndex(c => new { c.EmpresaId, c.Nivel, c.Time, c.UsuarioId, c.Chave, c.Origem }).IsUnique();
            e.HasOne(c => c.Empresa).WithMany().HasForeignKey(c => c.EmpresaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Autor).WithMany().HasForeignKey(c => c.AutorId).OnDelete(DeleteBehavior.Restrict);
        });

        // Os relatos dos usuários do dev.kit (US #602). No Azure SQL a tabela vem de api/scripts/sqlserver/Us602.sql. A
        // chave do cliente é ÚNICA por usuário quando veio (o reenvio da fila offline devolve o mesmo relato); a captura
        // é varbinary(max), com o teto do contrato conferido antes.
        modelBuilder.Entity<Relato>(e =>
        {
            e.ToTable("Relatos");
            e.Property(r => r.Categoria).HasMaxLength(20);
            e.Property(r => r.Estado).HasMaxLength(20);
            e.Property(r => r.Texto).HasMaxLength(Contracts.V1.RelatosV1.TamanhoMaximoDoTexto);
            e.Property(r => r.Contexto).HasMaxLength(Contracts.V1.RelatosV1.TamanhoMaximoDoContexto);
            e.Property(r => r.Resposta).HasMaxLength(Contracts.V1.RelatosV1.TamanhoMaximoDaResposta);
            e.Property(r => r.VersaoDevKit).HasMaxLength(Contracts.V1.RelatosV1.TamanhoMaximoDaVersao);
            e.Property(r => r.ChaveDoCliente).HasMaxLength(Contracts.V1.RelatosV1.TamanhoMaximoDaChave);
            e.HasIndex(r => new { r.UsuarioId, r.ChaveDoCliente }).IsUnique().HasFilter("[ChaveDoCliente] <> ''");
            e.HasIndex(r => new { r.EmpresaId, r.CriadoEmUtc });
            e.HasIndex(r => r.CriadoEmUtc);
            e.HasOne(r => r.Usuario).WithMany().HasForeignKey(r => r.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Empresa).WithMany().HasForeignKey(r => r.EmpresaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(r => r.RespondidoPor).WithMany().HasForeignKey(r => r.RespondidoPorId).OnDelete(DeleteBehavior.NoAction);
        });

        // O design compartilhado (US #669). No Azure SQL as tabelas vêm de api/scripts/sqlserver/Us669.sql. As imagens NÃO
        // ficam aqui: vão para o armazém (o Blob Storage), e a versão guarda a chave e o hash.
        modelBuilder.Entity<ProjetoDeDesign>(e =>
        {
            e.ToTable("ProjetosDeDesign");
            e.Property(p => p.Espaco).HasMaxLength(40);
            e.Property(p => p.Nivel).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoNivel);
            e.Property(p => p.Time).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoTime);
            e.Property(p => p.Repositorio).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDoRepositorio);
            e.Property(p => p.Chave).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDoRepositorio);
            // O UsuarioId é o dono do nível usuário e vale 0 nos outros: sem FK, como na convenção.
            e.HasIndex(p => new { p.Espaco, p.Nivel, p.Time, p.UsuarioId, p.Chave }).IsUnique();
            e.HasIndex(p => new { p.EmpresaId, p.AtualizadoEmUtc });
            e.HasIndex(p => new { p.UsuarioId, p.AtualizadoEmUtc });
            e.HasOne(p => p.Empresa).WithMany().HasForeignKey(p => p.EmpresaId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Telas).WithOne(t => t.Projeto).HasForeignKey(t => t.ProjetoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TelaDeDesign>(e =>
        {
            e.ToTable("TelasDeDesign");
            e.Property(t => t.Identificador).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDoId);
            e.Property(t => t.Nome).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDoNome);
            e.Property(t => t.Estado).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDoEstado);
            e.HasIndex(t => new { t.ProjetoId, t.Identificador }).IsUnique();
            e.HasMany(t => t.Versoes).WithOne(v => v.Tela).HasForeignKey(v => v.TelaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VersaoDeDesign>(e =>
        {
            e.ToTable("VersoesDeDesign");
            e.Property(v => v.Tipo).HasMaxLength(20);
            e.Property(v => v.Tema).HasMaxLength(10);
            e.Property(v => v.Origem).HasMaxLength(20);
            e.Property(v => v.Alvo).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDoAlvo);
            e.Property(v => v.Branch).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDoBranch);
            e.Property(v => v.Commit).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDoCommit);
            e.Property(v => v.Descricao).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDaDescricao);
            e.Property(v => v.AutorTipo).HasMaxLength(20);
            e.Property(v => v.Runner).HasMaxLength(Contracts.V1.DesignV1.TamanhoMaximoDoRunner);
            e.Property(v => v.Sha256).HasMaxLength(64);
            e.Property(v => v.ChaveDaImagem).HasMaxLength(300);
            // O número ÚNICO na tela é o que torna o reenvio da fila do dev.kit inofensivo (o hash decide 200 ou 409).
            e.HasIndex(v => new { v.TelaId, v.Numero }).IsUnique();
            e.HasOne(v => v.Autor).WithMany().HasForeignKey(v => v.AutorId).OnDelete(DeleteBehavior.Restrict);
        });

        // Os projetos compartilhados (US #693), no escopo do design. No Azure SQL as tabelas vêm de
        // api/scripts/sqlserver/Us693.sql.
        modelBuilder.Entity<ProjetoCompartilhado>(e =>
        {
            e.ToTable("ProjetosCompartilhados");
            e.Property(p => p.Espaco).HasMaxLength(40);
            e.Property(p => p.Nivel).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoNivel);
            e.Property(p => p.Time).HasMaxLength(RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoTime);
            e.Property(p => p.Slug).HasMaxLength(Contracts.V1.ProjetosV1.TamanhoMaximoDoSlug);
            e.Property(p => p.Nome).HasMaxLength(Contracts.V1.ProjetosV1.TamanhoMaximoDoNome);
            e.Property(p => p.Branch).HasMaxLength(Contracts.V1.ProjetosV1.TamanhoMaximoDoBranch);
            e.Property(p => p.DestinoDaPr).HasMaxLength(Contracts.V1.ProjetosV1.TamanhoMaximoDoBranch);
            e.Property(p => p.Situacao).HasMaxLength(20);
            // O UsuarioId é o dono do nível usuário e vale 0 nos outros: sem FK, como no design.
            e.HasIndex(p => new { p.Espaco, p.Nivel, p.Time, p.UsuarioId, p.Slug }).IsUnique();
            e.HasIndex(p => new { p.EmpresaId, p.AtualizadoEmUtc });
            e.HasOne(p => p.Empresa).WithMany().HasForeignKey(p => p.EmpresaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Autor).WithMany().HasForeignKey(p => p.AutorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(p => p.Itens).WithOne(i => i.Projeto).HasForeignKey(i => i.ProjetoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemDeProjetoCompartilhado>(e =>
        {
            e.ToTable("ItensDeProjetoCompartilhado");
            e.Property(i => i.Titulo).HasMaxLength(Contracts.V1.ProjetosV1.TamanhoMaximoDoNome);
            e.Property(i => i.Descricao).HasMaxLength(Contracts.V1.ProjetosV1.TamanhoMaximoDoTexto);
            e.Property(i => i.Fluxo).HasMaxLength(Contracts.V1.ProjetosV1.TamanhoMaximoDoNome);
            e.Property(i => i.Dependencias).HasMaxLength(Contracts.V1.ProjetosV1.TamanhoMaximoDoTexto);
            e.Property(i => i.Situacao).HasMaxLength(20);
            e.HasIndex(i => new { i.ProjetoId, i.Numero }).IsUnique();
        });

        // Os catálogos nomeados (US #461). No Azure SQL as tabelas vêm de api/scripts/sqlserver/Us461.sql.
        modelBuilder.Entity<Catalogo>(e =>
        {
            e.ToTable("Catalogos");
            e.Property(c => c.Slug).HasMaxLength(RegrasDosCatalogos.TamanhoMaximoDoSlug);
            e.Property(c => c.Nome).HasMaxLength(RegrasDosCatalogos.TamanhoMaximoDoNome);
            e.Property(c => c.Descricao).HasMaxLength(RegrasDosCatalogos.TamanhoMaximoDaDescricao);
            e.HasIndex(c => c.Slug).IsUnique();
        });

        modelBuilder.Entity<VinculoDeCatalogo>(e =>
        {
            e.ToTable("VinculosDeCatalogo");
            e.HasIndex(v => new { v.CatalogoId, v.EmpresaId }).IsUnique();
            e.HasIndex(v => v.EmpresaId);
            e.HasOne(v => v.Catalogo).WithMany().HasForeignKey(v => v.CatalogoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.Empresa).WithMany().HasForeignKey(v => v.EmpresaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemDoCatalogoEditavel>(e =>
        {
            e.ToTable("ItensEditaveisDeCatalogo");
            e.Property(i => i.Tipo).HasMaxLength(10);
            e.Property(i => i.Nome).HasMaxLength(RegrasDoCatalogoDaEmpresa.TamanhoMaximoDoNome);
            e.Property(i => i.Descricao).HasMaxLength(RegrasDosCatalogos.TamanhoMaximoDaDescricao);
            e.Property(i => i.Categoria).HasMaxLength(RegrasDosCatalogos.TamanhoMaximoDaCategoria);
            e.Property(i => i.Arquivo).HasMaxLength(200);
            // Até 256 KB de UTF-8 (a regra do Core): nvarchar(max) no SQL Server, como o EF gera sem tamanho.
            e.Property(i => i.Hash).HasMaxLength(64);
            e.Property(i => i.Dependencias).HasMaxLength(4000);
            e.Property(i => i.AlteradoPor).HasMaxLength(100);
            e.HasIndex(i => new { i.CatalogoId, i.Tipo, i.Nome }).IsUnique();
            e.HasOne(i => i.Catalogo).WithMany().HasForeignKey(i => i.CatalogoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VersaoDoCatalogo>(e =>
        {
            e.ToTable("VersoesDoCatalogo");
            e.Property(v => v.Tag).HasMaxLength(60);
            e.Property(v => v.Sha256).HasMaxLength(64);
            e.Property(v => v.PublicadaPor).HasMaxLength(100);
            e.HasIndex(v => new { v.CatalogoId, v.Versao }).IsUnique();
            e.HasOne(v => v.Catalogo).WithMany().HasForeignKey(v => v.CatalogoId).OnDelete(DeleteBehavior.Cascade);
        });

        // O zip à parte: a lista das releases não carrega os bytes (varbinary(max) no SQL Server).
        modelBuilder.Entity<PacoteDoCatalogo>(e =>
        {
            e.ToTable("PacotesDoCatalogo");
            e.HasKey(p => p.VersaoId);
            e.HasOne<VersaoDoCatalogo>().WithOne().HasForeignKey<PacoteDoCatalogo>(p => p.VersaoId).OnDelete(DeleteBehavior.Cascade);
        });

        // A conta do dev, o trial, a licença e o pagamento (US #425): o mesmo script Us425.sql no Azure SQL.
        modelBuilder.Entity<LinkDeSenha>(e =>
        {
            e.ToTable("LinksDeSenha");
            e.Property(l => l.TokenHash).HasMaxLength(64);
            e.HasIndex(l => l.TokenHash).IsUnique();
            e.HasOne(l => l.Usuario).WithMany().HasForeignKey(l => l.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrialDeMaquina>(e =>
        {
            e.ToTable("TrialsDeMaquina");
            e.Property(t => t.Impressao).HasMaxLength(64);
            e.HasIndex(t => t.Impressao).IsUnique();
        });

        modelBuilder.Entity<LicencaEmitida>(e =>
        {
            e.ToTable("LicencasEmitidas");
            e.Property(l => l.Tipo).HasMaxLength(20);
            e.HasIndex(l => new { l.MaquinaId, l.EmitidaEmUtc });
            e.HasOne(l => l.Maquina).WithMany().HasForeignKey(l => l.MaquinaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventoDePagamento>(e =>
        {
            e.ToTable("EventosDePagamento");
            e.Property(p => p.EventoId).HasMaxLength(100);
            e.Property(p => p.Tipo).HasMaxLength(50);
            // O id do evento ÚNICO é o que torna o reenvio do gateway inofensivo, como o eventId da telemetria.
            e.HasIndex(p => p.EventoId).IsUnique();
        });

        modelBuilder.Entity<AcessoAosDados>(e =>
        {
            e.ToTable("AcessosAosDados");
            e.Property(x => x.Login).HasMaxLength(ValidadorDeEmpresa.TamanhoMaximoDoLogin);
            e.Property(x => x.OQue).HasMaxLength(500);
            e.HasIndex(x => x.EmUtc);
        });

        modelBuilder.Entity<Maquina>(e =>
        {
            e.ToTable("Maquinas");
            e.Property(m => m.MaquinaId).HasMaxLength(64);
            e.Property(m => m.Apelido).HasMaxLength(100);
            e.Property(m => m.ChaveHash).HasMaxLength(64);
            e.Property(m => m.VersaoDevKit).HasMaxLength(50);
            e.Property(m => m.Colaborador).HasMaxLength(ValidadorDeEmpresa.TamanhoMaximoDoColaborador);
            e.Property(m => m.Impressao).HasMaxLength(64);
            e.HasIndex(m => m.MaquinaId).IsUnique();
            e.HasIndex(m => m.ChaveHash).IsUnique();
            e.HasIndex(m => m.EmpresaId);
            e.HasOne(m => m.Empresa).WithMany().HasForeignKey(m => m.EmpresaId).OnDelete(DeleteBehavior.SetNull);
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

        // As exceções não classificadas (US #381). No Azure SQL as tabelas nascem do script
        // api/scripts/sqlserver/GruposDeErro.sql, pelo mesmo motivo dos pedidos de demonstração.
        modelBuilder.Entity<GrupoDeErro>(e =>
        {
            e.ToTable("GruposDeErro");
            e.Property(g => g.Assinatura).HasMaxLength(RegrasDeErro.TamanhoMaximoDaAssinatura);
            e.Property(g => g.Tipo).HasMaxLength(ValidadorDeLote.TamanhoMaximoDoTexto);
            e.Property(g => g.Estado).HasMaxLength(20);
            e.Property(g => g.ResolvidoNaVersao).HasMaxLength(RegrasDeErro.TamanhoMaximoDaVersao);
            e.Property(g => g.PrimeiraVersao).HasMaxLength(50);
            e.Property(g => g.UltimaVersao).HasMaxLength(50);
            e.HasIndex(g => g.Assinatura).IsUnique();
            e.HasIndex(g => g.UltimoVistoEmUtc);
        });

        modelBuilder.Entity<OcorrenciaDeErro>(e =>
        {
            e.ToTable("OcorrenciasDeErro");
            e.Property(o => o.EventId).HasMaxLength(64);
            e.Property(o => o.VersaoDevKit).HasMaxLength(50);
            e.Property(o => o.Trace).HasMaxLength(RegrasDeErro.TamanhoMaximoDoTrace);
            e.HasIndex(o => new { o.GrupoId, o.EmUtc });
            e.HasIndex(o => o.EmUtc);
            e.HasOne(o => o.Grupo).WithMany().HasForeignKey(o => o.GrupoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.Maquina).WithMany().HasForeignKey(o => o.MaquinaId).OnDelete(DeleteBehavior.Cascade);
        });

        // A foto do ROI por work item (US #387): UMA por (máquina, item) — o índice único é o que garante
        // que o upsert da ingestão nunca deixa duas. No Azure SQL a tabela vem de
        // api/scripts/sqlserver/RoiDeWorkItem.sql.
        modelBuilder.Entity<RoiDeWorkItem>(e =>
        {
            e.ToTable("RoisDeWorkItem");
            e.Property(r => r.Tipo).HasMaxLength(RegrasDeRoi.TamanhoMaximoDoTexto);
            e.Property(r => r.Estado).HasMaxLength(RegrasDeRoi.TamanhoMaximoDoTexto);
            e.Property(r => r.Horas).HasPrecision(10, 2);
            e.Property(r => r.HorasNoBoard).HasPrecision(10, 2);
            e.Property(r => r.HorasNoTimesheet).HasPrecision(10, 2);
            e.Property(r => r.EventId).HasMaxLength(64);
            e.HasIndex(r => new { r.MaquinaId, r.WorkItemId }).IsUnique();
            e.HasIndex(r => r.EmUtc);
            e.HasOne(r => r.Maquina).WithMany().HasForeignKey(r => r.MaquinaId).OnDelete(DeleteBehavior.Cascade);
        });

        // A avaliação de entrega (US #417): UMA por (máquina, sessão, turno) — o índice único é o que garante
        // que avaliar de novo o mesmo turno substitui, e nunca duplica. No Azure SQL a tabela vem de
        // api/scripts/sqlserver/AvaliacoesDeEntrega.sql.
        modelBuilder.Entity<AvaliacaoDeEntrega>(e =>
        {
            e.ToTable("AvaliacoesDeEntrega");
            e.Property(a => a.SessaoId).HasMaxLength(RegrasDeAvaliacao.TamanhoMaximoDaSessao);
            e.Property(a => a.Motivo).HasMaxLength(ValidadorDeLote.TamanhoMaximoDoTexto);
            e.Property(a => a.Agente).HasMaxLength(RegrasDeAvaliacao.TamanhoMaximoDoAgente);
            e.Property(a => a.Modelo).HasMaxLength(RegrasDeAvaliacao.TamanhoMaximoDoModelo);
            e.Property(a => a.Fluxo).HasMaxLength(ValidadorDeLote.TamanhoMaximoDoTexto);
            e.Property(a => a.EventId).HasMaxLength(64);
            e.HasIndex(a => new { a.MaquinaId, a.SessaoId, a.Turno }).IsUnique();
            e.HasIndex(a => a.EmUtc);
            e.HasOne(a => a.Maquina).WithMany().HasForeignKey(a => a.MaquinaId).OnDelete(DeleteBehavior.Cascade);
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
