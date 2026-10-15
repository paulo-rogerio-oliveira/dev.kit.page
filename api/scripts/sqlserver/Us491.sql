-- A US #491 no Azure SQL: o catálogo de repositórios da empresa (o caminho e a descrição que o agente do dev.kit grava
-- ao terminar cada task) e as convenções da empresa e dos times (o nome do branch da task).
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito)
-- e espelha a migration Us491ConhecimentoDaEmpresa do SQLite. Rode-o ANTES de publicar a versão da API com as rotas
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

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_Chave' AND object_id = OBJECT_ID(N'[dbo].[ConvencoesDaEmpresa]'))
    CREATE UNIQUE INDEX [IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_Chave] ON [dbo].[ConvencoesDaEmpresa] ([EmpresaId], [Nivel], [Time], [Chave]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_AutorId' AND object_id = OBJECT_ID(N'[dbo].[ConvencoesDaEmpresa]'))
    CREATE INDEX [IX_ConvencoesDaEmpresa_AutorId] ON [dbo].[ConvencoesDaEmpresa] ([AutorId]);
GO
