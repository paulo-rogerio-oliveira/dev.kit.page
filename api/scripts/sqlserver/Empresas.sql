-- O plano empresarial (US #381) no Azure SQL: as empresas, a trilha de auditoria do acesso aos
-- dados dos colaboradores, o papel e a empresa do usuário (o gestor) e a adesão da máquina (a
-- empresa, o nome do colaborador e o instante do consentimento).
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema
-- só numa base VAZIA — numa base que já existe ele não cria tabela nem coluna nova. Este script é
-- idempotente (pode rodar de novo sem efeito) e espelha as migrations 20261007221204_Empresas e
-- 20261007235607_ConsentimentoPorDia do SQLite.
-- Rode-o ANTES de publicar a versão da API com o painel empresarial (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[Empresas]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Empresas] (
        [Id] int NOT NULL IDENTITY,
        [Nome] nvarchar(100) NOT NULL,
        [Plano] nvarchar(50) NOT NULL,
        [Assentos] int NOT NULL,
        [CodigoDeAdesao] nvarchar(20) NOT NULL,
        [CriadaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Empresas] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Empresas_CodigoDeAdesao' AND object_id = OBJECT_ID(N'[dbo].[Empresas]'))
    CREATE UNIQUE INDEX [IX_Empresas_CodigoDeAdesao] ON [dbo].[Empresas] ([CodigoDeAdesao]);
GO

IF OBJECT_ID(N'[dbo].[AcessosAosDados]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AcessosAosDados] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [Login] nvarchar(100) NOT NULL,
        [EmpresaId] int NULL,
        [OQue] nvarchar(500) NOT NULL,
        [Linhas] int NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AcessosAosDados] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcessosAosDados_EmUtc' AND object_id = OBJECT_ID(N'[dbo].[AcessosAosDados]'))
    CREATE INDEX [IX_AcessosAosDados_EmUtc] ON [dbo].[AcessosAosDados] ([EmUtc]);
GO

-- O usuário: o papel (o admin que já existe fica "admin") e a empresa do gestor.
IF COL_LENGTH(N'dbo.Usuarios', N'Papel') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [Papel] nvarchar(20) NOT NULL CONSTRAINT [DF_Usuarios_Papel] DEFAULT N'admin';
GO

IF COL_LENGTH(N'dbo.Usuarios', N'EmpresaId') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [EmpresaId] int NULL
        CONSTRAINT [FK_Usuarios_Empresas_EmpresaId] FOREIGN KEY REFERENCES [dbo].[Empresas] ([Id]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Usuarios_EmpresaId' AND object_id = OBJECT_ID(N'[dbo].[Usuarios]'))
    CREATE INDEX [IX_Usuarios_EmpresaId] ON [dbo].[Usuarios] ([EmpresaId]);
GO

-- A máquina: a empresa (sai do vínculo se a empresa for apagada), o colaborador e o consentimento.
IF COL_LENGTH(N'dbo.Maquinas', N'EmpresaId') IS NULL
    ALTER TABLE [dbo].[Maquinas] ADD [EmpresaId] int NULL
        CONSTRAINT [FK_Maquinas_Empresas_EmpresaId] FOREIGN KEY REFERENCES [dbo].[Empresas] ([Id]) ON DELETE SET NULL;
GO

IF COL_LENGTH(N'dbo.Maquinas', N'Colaborador') IS NULL
    ALTER TABLE [dbo].[Maquinas] ADD [Colaborador] nvarchar(100) NOT NULL CONSTRAINT [DF_Maquinas_Colaborador] DEFAULT N'';
GO

IF COL_LENGTH(N'dbo.Maquinas', N'ConsentiuEmUtc') IS NULL
    ALTER TABLE [dbo].[Maquinas] ADD [ConsentiuEmUtc] datetime2 NULL;
GO

-- O primeiro dia cujos totais o gestor vê (migration 20261007235607_ConsentimentoPorDia): o uso
-- anterior ao consentimento não vai para a empresa.
IF COL_LENGTH(N'dbo.Maquinas', N'DadosDesde') IS NULL
    ALTER TABLE [dbo].[Maquinas] ADD [DadosDesde] date NULL;
GO

UPDATE [dbo].[Maquinas] SET [DadosDesde] = CAST([ConsentiuEmUtc] AS date) WHERE [ConsentiuEmUtc] IS NOT NULL AND [DadosDesde] IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Maquinas_EmpresaId' AND object_id = OBJECT_ID(N'[dbo].[Maquinas]'))
    CREATE INDEX [IX_Maquinas_EmpresaId] ON [dbo].[Maquinas] ([EmpresaId]);
GO
