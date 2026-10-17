-- A US #491 no Azure SQL: o catálogo de repositórios da empresa (o caminho e a descrição que o agente do dev.kit grava
-- ao terminar cada task) e as convenções da empresa, dos times e dos usuários (o nome do branch da task).
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito)
-- e espelha as migrations Us491ConhecimentoDaEmpresa e Us491ConvencaoDoUsuario do SQLite. Rode-o ANTES de publicar a versão da API com as rotas
-- /api/empresa/repositorios e /api/empresa/convencoes (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[RepositoriosDaEmpresa]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RepositoriosDaEmpresa] (
        [Id] bigint NOT NULL IDENTITY,
        [EmpresaId] int NOT NULL,
        [Origem] nvarchar(400) NOT NULL,
        [Chave] nvarchar(400) NOT NULL,
        [Descricao] nvarchar(1000) NOT NULL,
        [AutorId] int NOT NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_RepositoriosDaEmpresa] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RepositoriosDaEmpresa_Empresas_EmpresaId] FOREIGN KEY ([EmpresaId]) REFERENCES [dbo].[Empresas] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RepositoriosDaEmpresa_Usuarios_AutorId] FOREIGN KEY ([AutorId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RepositoriosDaEmpresa_EmpresaId_Chave' AND object_id = OBJECT_ID(N'[dbo].[RepositoriosDaEmpresa]'))
    CREATE UNIQUE INDEX [IX_RepositoriosDaEmpresa_EmpresaId_Chave] ON [dbo].[RepositoriosDaEmpresa] ([EmpresaId], [Chave]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RepositoriosDaEmpresa_AutorId' AND object_id = OBJECT_ID(N'[dbo].[RepositoriosDaEmpresa]'))
    CREATE INDEX [IX_RepositoriosDaEmpresa_AutorId] ON [dbo].[RepositoriosDaEmpresa] ([AutorId]);
GO

IF OBJECT_ID(N'[dbo].[ConvencoesDaEmpresa]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ConvencoesDaEmpresa] (
        [Id] bigint NOT NULL IDENTITY,
        [EmpresaId] int NOT NULL,
        [Nivel] nvarchar(10) NOT NULL,
        [Time] nvarchar(80) NOT NULL,
        [UsuarioId] int NOT NULL,
        [Chave] nvarchar(60) NOT NULL,
        [Valor] nvarchar(200) NOT NULL,
        [AutorId] int NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ConvencoesDaEmpresa] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ConvencoesDaEmpresa_Empresas_EmpresaId] FOREIGN KEY ([EmpresaId]) REFERENCES [dbo].[Empresas] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ConvencoesDaEmpresa_Usuarios_AutorId] FOREIGN KEY ([AutorId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

-- A convenção do USUÁRIO (migration Us491ConvencaoDoUsuario): numa base que rodou a primeira versão deste script, a
-- tabela já existe sem a coluna — ela entra com 0 (as da empresa e dos times) e o índice único passa a incluí-la.
IF COL_LENGTH(N'[dbo].[ConvencoesDaEmpresa]', N'UsuarioId') IS NULL
    ALTER TABLE [dbo].[ConvencoesDaEmpresa] ADD [UsuarioId] int NOT NULL CONSTRAINT [DF_ConvencoesDaEmpresa_UsuarioId] DEFAULT 0;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_Chave' AND object_id = OBJECT_ID(N'[dbo].[ConvencoesDaEmpresa]'))
    DROP INDEX [IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_Chave] ON [dbo].[ConvencoesDaEmpresa];
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave' AND object_id = OBJECT_ID(N'[dbo].[ConvencoesDaEmpresa]'))
    CREATE UNIQUE INDEX [IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave] ON [dbo].[ConvencoesDaEmpresa] ([EmpresaId], [Nivel], [Time], [UsuarioId], [Chave]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_AutorId' AND object_id = OBJECT_ID(N'[dbo].[ConvencoesDaEmpresa]'))
    CREATE INDEX [IX_ConvencoesDaEmpresa_AutorId] ON [dbo].[ConvencoesDaEmpresa] ([AutorId]);
GO
