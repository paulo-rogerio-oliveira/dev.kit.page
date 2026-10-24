-- A US #669 no Azure SQL: o DESIGN compartilhado — os projetos por repositório (num nível: empresa, time ou usuário),
-- as telas e as versões. As imagens NÃO ficam no banco: vão para o Azure Blob Storage (Design:Armazem=blob), e a
-- versão guarda a chave do blob e o hash.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito)
-- e espelha a migration Us669DesignCompartilhado do SQLite. Rode-o ANTES de publicar a versão da API com as rotas
-- /api/design (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[ProjetosDeDesign]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ProjetosDeDesign] (
        [Id] bigint NOT NULL IDENTITY,
        [EmpresaId] int NULL,
        [Espaco] nvarchar(40) NOT NULL,
        [Nivel] nvarchar(10) NOT NULL,
        [Time] nvarchar(80) NOT NULL,
        [UsuarioId] int NOT NULL,
        [Repositorio] nvarchar(400) NOT NULL,
        [Chave] nvarchar(400) NOT NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProjetosDeDesign] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProjetosDeDesign_Empresas_EmpresaId] FOREIGN KEY ([EmpresaId]) REFERENCES [dbo].[Empresas] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID(N'[dbo].[TelasDeDesign]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TelasDeDesign] (
        [Id] bigint NOT NULL IDENTITY,
        [ProjetoId] bigint NOT NULL,
        [Identificador] nvarchar(96) NOT NULL,
        [Nome] nvarchar(200) NOT NULL,
        [Estado] nvarchar(60) NOT NULL,
        CONSTRAINT [PK_TelasDeDesign] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TelasDeDesign_ProjetosDeDesign_ProjetoId] FOREIGN KEY ([ProjetoId]) REFERENCES [dbo].[ProjetosDeDesign] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID(N'[dbo].[VersoesDeDesign]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VersoesDeDesign] (
        [Id] bigint NOT NULL IDENTITY,
        [TelaId] bigint NOT NULL,
        [Numero] int NOT NULL,
        [Tipo] nvarchar(20) NOT NULL,
        [Base] int NULL,
        [Tema] nvarchar(10) NOT NULL,
        [Largura] int NOT NULL,
        [Altura] int NOT NULL,
        [Origem] nvarchar(20) NOT NULL,
        [Alvo] nvarchar(1000) NOT NULL,
        [Branch] nvarchar(250) NOT NULL,
        [Commit] nvarchar(64) NOT NULL,
        [Descricao] nvarchar(2000) NOT NULL,
        [AutorId] int NOT NULL,
        [AutorTipo] nvarchar(20) NOT NULL,
        [Runner] nvarchar(60) NOT NULL,
        [CriadaEmUtc] datetime2 NOT NULL,
        [EnviadaEmUtc] datetime2 NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [Bytes] bigint NOT NULL,
        [ChaveDaImagem] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_VersoesDeDesign] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VersoesDeDesign_TelasDeDesign_TelaId] FOREIGN KEY ([TelaId]) REFERENCES [dbo].[TelasDeDesign] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_VersoesDeDesign_Usuarios_AutorId] FOREIGN KEY ([AutorId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

-- O projeto é único por (espaço do teto, nível, time, dono, repositório normalizado).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjetosDeDesign_Espaco_Nivel_Time_UsuarioId_Chave' AND object_id = OBJECT_ID(N'[dbo].[ProjetosDeDesign]'))
    CREATE UNIQUE INDEX [IX_ProjetosDeDesign_Espaco_Nivel_Time_UsuarioId_Chave] ON [dbo].[ProjetosDeDesign] ([Espaco], [Nivel], [Time], [UsuarioId], [Chave]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjetosDeDesign_EmpresaId_AtualizadoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[ProjetosDeDesign]'))
    CREATE INDEX [IX_ProjetosDeDesign_EmpresaId_AtualizadoEmUtc] ON [dbo].[ProjetosDeDesign] ([EmpresaId], [AtualizadoEmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjetosDeDesign_UsuarioId_AtualizadoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[ProjetosDeDesign]'))
    CREATE INDEX [IX_ProjetosDeDesign_UsuarioId_AtualizadoEmUtc] ON [dbo].[ProjetosDeDesign] ([UsuarioId], [AtualizadoEmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TelasDeDesign_ProjetoId_Identificador' AND object_id = OBJECT_ID(N'[dbo].[TelasDeDesign]'))
    CREATE UNIQUE INDEX [IX_TelasDeDesign_ProjetoId_Identificador] ON [dbo].[TelasDeDesign] ([ProjetoId], [Identificador]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VersoesDeDesign_AutorId' AND object_id = OBJECT_ID(N'[dbo].[VersoesDeDesign]'))
    CREATE INDEX [IX_VersoesDeDesign_AutorId] ON [dbo].[VersoesDeDesign] ([AutorId]);
GO

-- O número único na tela torna o reenvio da fila offline do dev.kit inofensivo (o hash decide 200 ou 409).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VersoesDeDesign_TelaId_Numero' AND object_id = OBJECT_ID(N'[dbo].[VersoesDeDesign]'))
    CREATE UNIQUE INDEX [IX_VersoesDeDesign_TelaId_Numero] ON [dbo].[VersoesDeDesign] ([TelaId], [Numero]);
GO
