-- A US #461 no Azure SQL: os catálogos nomeados (o básico e os que o admin cria), o vínculo deles com as empresas,
-- os itens editáveis (o rascunho de skill, Tech Plan e fluxo), as releases publicadas pelo site e o zip de cada uma.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito)
-- e espelha a migration 20261013131005_Us461CatalogosNomeados do SQLite. Rode-o ANTES de publicar a versão da API com
-- os catálogos (ver docs/publicacao-azure.md). O registro do catálogo básico também é semeado pela API na subida; o
-- INSERT do fim só o adianta.

IF OBJECT_ID(N'[dbo].[Catalogos]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Catalogos] (
        [Id] int NOT NULL IDENTITY,
        [Slug] nvarchar(40) NOT NULL,
        [Nome] nvarchar(80) NOT NULL,
        [Descricao] nvarchar(500) NOT NULL,
        [Basico] bit NOT NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Catalogos] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Catalogos_Slug' AND object_id = OBJECT_ID(N'[dbo].[Catalogos]'))
    CREATE UNIQUE INDEX [IX_Catalogos_Slug] ON [dbo].[Catalogos] ([Slug]);
GO

IF OBJECT_ID(N'[dbo].[VinculosDeCatalogo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VinculosDeCatalogo] (
        [Id] bigint NOT NULL IDENTITY,
        [CatalogoId] int NOT NULL,
        [EmpresaId] int NOT NULL,
        [RelacionadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_VinculosDeCatalogo] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VinculosDeCatalogo_Catalogos_CatalogoId] FOREIGN KEY ([CatalogoId]) REFERENCES [dbo].[Catalogos] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_VinculosDeCatalogo_Empresas_EmpresaId] FOREIGN KEY ([EmpresaId]) REFERENCES [dbo].[Empresas] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VinculosDeCatalogo_CatalogoId_EmpresaId' AND object_id = OBJECT_ID(N'[dbo].[VinculosDeCatalogo]'))
    CREATE UNIQUE INDEX [IX_VinculosDeCatalogo_CatalogoId_EmpresaId] ON [dbo].[VinculosDeCatalogo] ([CatalogoId], [EmpresaId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VinculosDeCatalogo_EmpresaId' AND object_id = OBJECT_ID(N'[dbo].[VinculosDeCatalogo]'))
    CREATE INDEX [IX_VinculosDeCatalogo_EmpresaId] ON [dbo].[VinculosDeCatalogo] ([EmpresaId]);
GO

-- O conteúdo vai até 256 KB (a regra do Core): nvarchar(max), como o EF gera sem tamanho.
IF OBJECT_ID(N'[dbo].[ItensEditaveisDeCatalogo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItensEditaveisDeCatalogo] (
        [Id] bigint NOT NULL IDENTITY,
        [CatalogoId] int NOT NULL,
        [Tipo] nvarchar(10) NOT NULL,
        [Nome] nvarchar(64) NOT NULL,
        [Descricao] nvarchar(500) NOT NULL,
        [Categoria] nvarchar(60) NOT NULL,
        [Arquivo] nvarchar(200) NOT NULL,
        [Conteudo] nvarchar(max) NOT NULL,
        [Hash] nvarchar(64) NOT NULL,
        [Dependencias] nvarchar(4000) NOT NULL,
        [AlteradoEmUtc] datetime2 NOT NULL,
        [AlteradoPor] nvarchar(100) NOT NULL,
        [RetiradoEmUtc] datetime2 NULL,
        CONSTRAINT [PK_ItensEditaveisDeCatalogo] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ItensEditaveisDeCatalogo_Catalogos_CatalogoId] FOREIGN KEY ([CatalogoId]) REFERENCES [dbo].[Catalogos] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItensEditaveisDeCatalogo_CatalogoId_Tipo_Nome' AND object_id = OBJECT_ID(N'[dbo].[ItensEditaveisDeCatalogo]'))
    CREATE UNIQUE INDEX [IX_ItensEditaveisDeCatalogo_CatalogoId_Tipo_Nome] ON [dbo].[ItensEditaveisDeCatalogo] ([CatalogoId], [Tipo], [Nome]);
GO

IF OBJECT_ID(N'[dbo].[VersoesDoCatalogo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VersoesDoCatalogo] (
        [Id] bigint NOT NULL IDENTITY,
        [CatalogoId] int NOT NULL,
        [Versao] int NOT NULL,
        [Tag] nvarchar(60) NOT NULL,
        [Manifesto] nvarchar(max) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [TamanhoBytes] bigint NOT NULL,
        [Itens] int NOT NULL,
        [Retirados] int NOT NULL,
        [PublicadaEmUtc] datetime2 NOT NULL,
        [PublicadaPor] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_VersoesDoCatalogo] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VersoesDoCatalogo_Catalogos_CatalogoId] FOREIGN KEY ([CatalogoId]) REFERENCES [dbo].[Catalogos] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VersoesDoCatalogo_CatalogoId_Versao' AND object_id = OBJECT_ID(N'[dbo].[VersoesDoCatalogo]'))
    CREATE UNIQUE INDEX [IX_VersoesDoCatalogo_CatalogoId_Versao] ON [dbo].[VersoesDoCatalogo] ([CatalogoId], [Versao]);
GO

-- O zip à parte (o IArmazemDePacotes no banco): a lista das releases não carrega os bytes.
IF OBJECT_ID(N'[dbo].[PacotesDoCatalogo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PacotesDoCatalogo] (
        [VersaoId] bigint NOT NULL,
        [Zip] varbinary(max) NOT NULL,
        CONSTRAINT [PK_PacotesDoCatalogo] PRIMARY KEY ([VersaoId]),
        CONSTRAINT [FK_PacotesDoCatalogo_VersoesDoCatalogo_VersaoId] FOREIGN KEY ([VersaoId]) REFERENCES [dbo].[VersoesDoCatalogo] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Catalogos] WHERE [Basico] = 1)
    INSERT INTO [dbo].[Catalogos] ([Slug], [Nome], [Descricao], [Basico], [CriadoEmUtc])
    VALUES (N'basico', N'Básico', N'Skills, tech plans e fluxos que vêm com o dev.kit — de todas as empresas.', 1, SYSUTCDATETIME());
GO
