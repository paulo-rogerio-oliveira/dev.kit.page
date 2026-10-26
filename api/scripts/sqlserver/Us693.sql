-- A US #693 no Azure SQL: os PROJETOS do dev.kit compartilhados por escopo (empresa, time ou usuário) — o projeto
-- (repositórios, branch, definição, autonomia, paralelismo, destino da PR) e os itens do plano (fluxo, componentes,
-- dependências e onda).
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito)
-- e espelha a migration Us693ProjetosCompartilhados do SQLite. Rode-o ANTES de publicar a versão da API com as rotas
-- /api/projetos (ver docs/publicacao-azure.md) — e ANTES do pacote do dev.kit que as chama (Rule de impacto 5).

IF OBJECT_ID(N'[dbo].[ProjetosCompartilhados]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ProjetosCompartilhados] (
        [Id] bigint NOT NULL IDENTITY,
        [EmpresaId] int NULL,
        [Espaco] nvarchar(40) NOT NULL,
        [Nivel] nvarchar(10) NOT NULL,
        [Time] nvarchar(80) NOT NULL,
        [UsuarioId] int NOT NULL,
        [Slug] nvarchar(60) NOT NULL,
        [Nome] nvarchar(200) NOT NULL,
        [Repositorios] nvarchar(max) NOT NULL,
        [Branch] nvarchar(250) NOT NULL,
        [SemanaConvertida] bit NOT NULL,
        [Definicao] nvarchar(max) NOT NULL,
        [Anexos] nvarchar(max) NOT NULL,
        [Autonomia] bit NOT NULL,
        [Paralelismo] int NOT NULL,
        [DestinoDaPr] nvarchar(250) NOT NULL,
        [Situacao] nvarchar(20) NOT NULL,
        [Versao] int NOT NULL,
        [AutorId] int NOT NULL,
        [AtualizadoPorId] int NOT NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProjetosCompartilhados] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProjetosCompartilhados_Empresas_EmpresaId] FOREIGN KEY ([EmpresaId]) REFERENCES [dbo].[Empresas] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProjetosCompartilhados_Usuarios_AutorId] FOREIGN KEY ([AutorId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

IF OBJECT_ID(N'[dbo].[ItensDeProjetoCompartilhado]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItensDeProjetoCompartilhado] (
        [Id] bigint NOT NULL IDENTITY,
        [ProjetoId] bigint NOT NULL,
        [Numero] int NOT NULL,
        [Titulo] nvarchar(200) NOT NULL,
        [Descricao] nvarchar(2000) NOT NULL,
        [Fluxo] nvarchar(200) NOT NULL,
        [Repositorios] nvarchar(max) NOT NULL,
        [Componentes] nvarchar(max) NOT NULL,
        [Dependencias] nvarchar(2000) NOT NULL,
        [Onda] int NOT NULL,
        [WorkItemId] int NOT NULL,
        [Situacao] nvarchar(20) NOT NULL,
        CONSTRAINT [PK_ItensDeProjetoCompartilhado] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ItensDeProjetoCompartilhado_ProjetosCompartilhados_ProjetoId] FOREIGN KEY ([ProjetoId]) REFERENCES [dbo].[ProjetosCompartilhados] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- O projeto é único por (espaço, nível, time, dono, slug): o slug é o {SLUG} do branch.projeto.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjetosCompartilhados_Espaco_Nivel_Time_UsuarioId_Slug' AND object_id = OBJECT_ID(N'[dbo].[ProjetosCompartilhados]'))
    CREATE UNIQUE INDEX [IX_ProjetosCompartilhados_Espaco_Nivel_Time_UsuarioId_Slug] ON [dbo].[ProjetosCompartilhados] ([Espaco], [Nivel], [Time], [UsuarioId], [Slug]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjetosCompartilhados_EmpresaId_AtualizadoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[ProjetosCompartilhados]'))
    CREATE INDEX [IX_ProjetosCompartilhados_EmpresaId_AtualizadoEmUtc] ON [dbo].[ProjetosCompartilhados] ([EmpresaId], [AtualizadoEmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjetosCompartilhados_AutorId' AND object_id = OBJECT_ID(N'[dbo].[ProjetosCompartilhados]'))
    CREATE INDEX [IX_ProjetosCompartilhados_AutorId] ON [dbo].[ProjetosCompartilhados] ([AutorId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItensDeProjetoCompartilhado_ProjetoId_Numero' AND object_id = OBJECT_ID(N'[dbo].[ItensDeProjetoCompartilhado]'))
    CREATE UNIQUE INDEX [IX_ItensDeProjetoCompartilhado_ProjetoId_Numero] ON [dbo].[ItensDeProjetoCompartilhado] ([ProjetoId], [Numero]);
GO
