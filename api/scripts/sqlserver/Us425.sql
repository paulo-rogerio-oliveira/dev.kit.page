-- A US #425 no Azure SQL: o catálogo da empresa (itens, versões até 256 KB e assinaturas), a conta do dev
-- (e-mail e link de definir a senha), a política de login por empresa, o trial pela impressão da máquina, as
-- licenças assinadas emitidas e os avisos do gateway de pagamento.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nem coluna nova. Este script é idempotente (pode rodar de
-- novo sem efeito) e espelha a migration 20261010195024_Us425CatalogoContaELicenca do SQLite. Rode-o ANTES de
-- publicar a versão da API com o catálogo e a conta (ver docs/publicacao-azure.md).

-- ----- Colunas novas -----

IF COL_LENGTH(N'dbo.Usuarios', N'Email') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [Email] nvarchar(254) NOT NULL CONSTRAINT [DF_Usuarios_Email] DEFAULT N'';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Usuarios_Email' AND object_id = OBJECT_ID(N'[dbo].[Usuarios]'))
    CREATE INDEX [IX_Usuarios_Email] ON [dbo].[Usuarios] ([Email]);
GO

IF COL_LENGTH(N'dbo.Maquinas', N'Impressao') IS NULL
    ALTER TABLE [dbo].[Maquinas] ADD [Impressao] nvarchar(64) NOT NULL CONSTRAINT [DF_Maquinas_Impressao] DEFAULT N'';
GO

IF COL_LENGTH(N'dbo.Empresas', N'LoginObrigatorio') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [LoginObrigatorio] bit NULL;
GO

IF COL_LENGTH(N'dbo.Empresas', N'Individual') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [Individual] bit NOT NULL CONSTRAINT [DF_Empresas_Individual] DEFAULT CAST(0 AS bit);
GO

IF COL_LENGTH(N'dbo.Empresas', N'AssinaturaExterna') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [AssinaturaExterna] nvarchar(100) NOT NULL CONSTRAINT [DF_Empresas_AssinaturaExterna] DEFAULT N'';
GO

IF COL_LENGTH(N'dbo.Empresas', N'AssinaturaCanceladaEmUtc') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [AssinaturaCanceladaEmUtc] datetime2 NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Empresas_AssinaturaExterna' AND object_id = OBJECT_ID(N'[dbo].[Empresas]'))
    CREATE INDEX [IX_Empresas_AssinaturaExterna] ON [dbo].[Empresas] ([AssinaturaExterna]);
GO

-- ----- O catálogo da empresa -----

IF OBJECT_ID(N'[dbo].[ItensDeCatalogo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItensDeCatalogo] (
        [Id] bigint NOT NULL IDENTITY,
        [EmpresaId] int NOT NULL,
        [Tipo] nvarchar(10) NOT NULL,
        [Nome] nvarchar(64) NOT NULL,
        [Descricao] nvarchar(500) NOT NULL,
        [AutorId] int NOT NULL,
        [VersaoAtual] int NOT NULL,
        [HashAtual] nvarchar(64) NOT NULL,
        [Dependencias] nvarchar(4000) NOT NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        [RetiradoEmUtc] datetime2 NULL,
        CONSTRAINT [PK_ItensDeCatalogo] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ItensDeCatalogo_Empresas_EmpresaId] FOREIGN KEY ([EmpresaId]) REFERENCES [dbo].[Empresas] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ItensDeCatalogo_Usuarios_AutorId] FOREIGN KEY ([AutorId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItensDeCatalogo_EmpresaId_Tipo_Nome' AND object_id = OBJECT_ID(N'[dbo].[ItensDeCatalogo]'))
    CREATE UNIQUE INDEX [IX_ItensDeCatalogo_EmpresaId_Tipo_Nome] ON [dbo].[ItensDeCatalogo] ([EmpresaId], [Tipo], [Nome]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItensDeCatalogo_AutorId' AND object_id = OBJECT_ID(N'[dbo].[ItensDeCatalogo]'))
    CREATE INDEX [IX_ItensDeCatalogo_AutorId] ON [dbo].[ItensDeCatalogo] ([AutorId]);
GO

-- O conteúdo vai até 256 KB (a regra do Core): nvarchar(max), como o EF gera sem tamanho.
IF OBJECT_ID(N'[dbo].[VersoesDeItem]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VersoesDeItem] (
        [Id] bigint NOT NULL IDENTITY,
        [ItemId] bigint NOT NULL,
        [Versao] int NOT NULL,
        [Hash] nvarchar(64) NOT NULL,
        [Conteudo] nvarchar(max) NOT NULL,
        [AutorId] int NOT NULL,
        [PublicadaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_VersoesDeItem] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VersoesDeItem_ItensDeCatalogo_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[ItensDeCatalogo] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VersoesDeItem_ItemId_Versao' AND object_id = OBJECT_ID(N'[dbo].[VersoesDeItem]'))
    CREATE UNIQUE INDEX [IX_VersoesDeItem_ItemId_Versao] ON [dbo].[VersoesDeItem] ([ItemId], [Versao]);
GO

IF OBJECT_ID(N'[dbo].[AssinaturasDeCatalogo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AssinaturasDeCatalogo] (
        [Id] bigint NOT NULL IDENTITY,
        [ItemId] bigint NOT NULL,
        [UsuarioId] int NOT NULL,
        [AssinadaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AssinaturasDeCatalogo] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AssinaturasDeCatalogo_ItensDeCatalogo_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[ItensDeCatalogo] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AssinaturasDeCatalogo_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssinaturasDeCatalogo_ItemId_UsuarioId' AND object_id = OBJECT_ID(N'[dbo].[AssinaturasDeCatalogo]'))
    CREATE UNIQUE INDEX [IX_AssinaturasDeCatalogo_ItemId_UsuarioId] ON [dbo].[AssinaturasDeCatalogo] ([ItemId], [UsuarioId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssinaturasDeCatalogo_UsuarioId' AND object_id = OBJECT_ID(N'[dbo].[AssinaturasDeCatalogo]'))
    CREATE INDEX [IX_AssinaturasDeCatalogo_UsuarioId] ON [dbo].[AssinaturasDeCatalogo] ([UsuarioId]);
GO

-- ----- A conta, o trial, a licença e o pagamento -----

IF OBJECT_ID(N'[dbo].[LinksDeSenha]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LinksDeSenha] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        [ExpiraEmUtc] datetime2 NOT NULL,
        [UsadoEmUtc] datetime2 NULL,
        CONSTRAINT [PK_LinksDeSenha] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LinksDeSenha_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LinksDeSenha_TokenHash' AND object_id = OBJECT_ID(N'[dbo].[LinksDeSenha]'))
    CREATE UNIQUE INDEX [IX_LinksDeSenha_TokenHash] ON [dbo].[LinksDeSenha] ([TokenHash]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LinksDeSenha_UsuarioId' AND object_id = OBJECT_ID(N'[dbo].[LinksDeSenha]'))
    CREATE INDEX [IX_LinksDeSenha_UsuarioId] ON [dbo].[LinksDeSenha] ([UsuarioId]);
GO

IF OBJECT_ID(N'[dbo].[TrialsDeMaquina]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TrialsDeMaquina] (
        [Id] bigint NOT NULL IDENTITY,
        [Impressao] nvarchar(64) NOT NULL,
        [IniciadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_TrialsDeMaquina] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TrialsDeMaquina_Impressao' AND object_id = OBJECT_ID(N'[dbo].[TrialsDeMaquina]'))
    CREATE UNIQUE INDEX [IX_TrialsDeMaquina_Impressao] ON [dbo].[TrialsDeMaquina] ([Impressao]);
GO

IF OBJECT_ID(N'[dbo].[LicencasEmitidas]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicencasEmitidas] (
        [Id] bigint NOT NULL IDENTITY,
        [MaquinaId] int NOT NULL,
        [UsuarioId] int NULL,
        [Tipo] nvarchar(20) NOT NULL,
        [EmitidaEmUtc] datetime2 NOT NULL,
        [ValidaAteUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_LicencasEmitidas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LicencasEmitidas_Maquinas_MaquinaId] FOREIGN KEY ([MaquinaId]) REFERENCES [dbo].[Maquinas] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LicencasEmitidas_MaquinaId_EmitidaEmUtc' AND object_id = OBJECT_ID(N'[dbo].[LicencasEmitidas]'))
    CREATE INDEX [IX_LicencasEmitidas_MaquinaId_EmitidaEmUtc] ON [dbo].[LicencasEmitidas] ([MaquinaId], [EmitidaEmUtc]);
GO

IF OBJECT_ID(N'[dbo].[EventosDePagamento]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EventosDePagamento] (
        [Id] bigint NOT NULL IDENTITY,
        [EventoId] nvarchar(100) NOT NULL,
        [Tipo] nvarchar(50) NOT NULL,
        [RecebidoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_EventosDePagamento] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventosDePagamento_EventoId' AND object_id = OBJECT_ID(N'[dbo].[EventosDePagamento]'))
    CREATE UNIQUE INDEX [IX_EventosDePagamento_EventoId] ON [dbo].[EventosDePagamento] ([EventoId]);
GO
