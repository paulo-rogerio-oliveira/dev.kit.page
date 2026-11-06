-- A provisão no Azure, no Azure SQL: a permissão por usuário, o recurso (que é também o pedido repetível) e a auditoria.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito)
-- e espelha a migration ProvisaoNoAzure do SQLite. Rode-o ANTES de publicar a versão da API com as rotas /api/provisao
-- e ANTES de ligar o worker DevKitPage.Provisao (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[PermissoesDeUsuario]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PermissoesDeUsuario] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [Recurso] nvarchar(40) NOT NULL,
        [ConcedidaPor] nvarchar(100) NOT NULL,
        [ConcedidaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PermissoesDeUsuario] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PermissoesDeUsuario_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- Uma liberação por (usuário, recurso).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PermissoesDeUsuario_UsuarioId_Recurso' AND object_id = OBJECT_ID(N'[dbo].[PermissoesDeUsuario]'))
    CREATE UNIQUE INDEX [IX_PermissoesDeUsuario_UsuarioId_Recurso] ON [dbo].[PermissoesDeUsuario] ([UsuarioId], [Recurso]);
GO

IF OBJECT_ID(N'[dbo].[RecursosProvisionados]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RecursosProvisionados] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [Tipo] nvarchar(20) NOT NULL,
        [Vaga] int NOT NULL,
        [Nome] nvarchar(60) NOT NULL,
        [Rotulo] nvarchar(20) NOT NULL,
        [Plano] nvarchar(20) NOT NULL,
        [NomeDoPlano] nvarchar(60) NOT NULL,
        [CustoMensal] decimal(10,2) NOT NULL,
        [Passos] nvarchar(100) NOT NULL,
        [Fila] nvarchar(63) NOT NULL,
        [Regiao] nvarchar(40) NOT NULL,
        [GrupoDeRecursos] nvarchar(90) NOT NULL,
        [Operacao] nvarchar(20) NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        [Tentativas] int NOT NULL,
        [Erro] nvarchar(500) NOT NULL,
        [IdNoAzure] nvarchar(400) NOT NULL,
        [Endpoint] nvarchar(300) NOT NULL,
        [ProximaTentativaEmUtc] datetime2 NULL,
        [ReservadoPor] nvarchar(100) NOT NULL,
        [ReservadoAteUtc] datetime2 NULL,
        [Versao] int NOT NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        [ProntoEmUtc] datetime2 NULL,
        [RemovidoEmUtc] datetime2 NULL,
        CONSTRAINT [PK_RecursosProvisionados] PRIMARY KEY ([Id]),
        -- O recurso pago não some com o usuário (sem ON DELETE).
        CONSTRAINT [FK_RecursosProvisionados_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

-- O nome é global no Azure: o índice único barra o sorteio repetido antes de o worker o pedir.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecursosProvisionados_Nome' AND object_id = OBJECT_ID(N'[dbo].[RecursosProvisionados]'))
    CREATE UNIQUE INDEX [IX_RecursosProvisionados_Nome] ON [dbo].[RecursosProvisionados] ([Nome]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecursosProvisionados_UsuarioId_Estado' AND object_id = OBJECT_ID(N'[dbo].[RecursosProvisionados]'))
    CREATE INDEX [IX_RecursosProvisionados_UsuarioId_Estado] ON [dbo].[RecursosProvisionados] ([UsuarioId], [Estado]);
GO

-- Item 12 (App Service e Static Web App): as colunas novas na base que já tinha a tabela do item 4. A migration
-- AplicativosNoAzure é o espelho delas no SQLite.
IF COL_LENGTH(N'dbo.RecursosProvisionados', N'Vaga') IS NULL
    ALTER TABLE [dbo].[RecursosProvisionados] ADD [Vaga] int NOT NULL CONSTRAINT [DF_RecursosProvisionados_Vaga] DEFAULT 0;
GO
IF COL_LENGTH(N'dbo.RecursosProvisionados', N'Rotulo') IS NULL
    ALTER TABLE [dbo].[RecursosProvisionados] ADD [Rotulo] nvarchar(20) NOT NULL CONSTRAINT [DF_RecursosProvisionados_Rotulo] DEFAULT N'';
GO
IF COL_LENGTH(N'dbo.RecursosProvisionados', N'Plano') IS NULL
    ALTER TABLE [dbo].[RecursosProvisionados] ADD [Plano] nvarchar(20) NOT NULL CONSTRAINT [DF_RecursosProvisionados_Plano] DEFAULT N'';
GO
IF COL_LENGTH(N'dbo.RecursosProvisionados', N'NomeDoPlano') IS NULL
    ALTER TABLE [dbo].[RecursosProvisionados] ADD [NomeDoPlano] nvarchar(60) NOT NULL CONSTRAINT [DF_RecursosProvisionados_NomeDoPlano] DEFAULT N'';
GO
IF COL_LENGTH(N'dbo.RecursosProvisionados', N'CustoMensal') IS NULL
    ALTER TABLE [dbo].[RecursosProvisionados] ADD [CustoMensal] decimal(10,2) NOT NULL CONSTRAINT [DF_RecursosProvisionados_CustoMensal] DEFAULT 0;
GO
IF COL_LENGTH(N'dbo.RecursosProvisionados', N'Passos') IS NULL
    ALTER TABLE [dbo].[RecursosProvisionados] ADD [Passos] nvarchar(100) NOT NULL CONSTRAINT [DF_RecursosProvisionados_Passos] DEFAULT N'';
GO

-- O nome do App Service vai a 60 caracteres (a conta de armazenamento tinha 24). Aumentar um nvarchar indexado é permitido.
IF COL_LENGTH(N'dbo.RecursosProvisionados', N'Nome') < 120
    ALTER TABLE [dbo].[RecursosProvisionados] ALTER COLUMN [Nome] nvarchar(60) NOT NULL;
GO

-- A vaga dos recursos ativos que já existiam (as filas do item 4): 1, 2, ... por usuário e tipo, na ordem do pedido.
UPDATE r SET [Vaga] = n.[Vaga]
FROM [dbo].[RecursosProvisionados] r
JOIN (SELECT [Id], ROW_NUMBER() OVER (PARTITION BY [UsuarioId], [Tipo] ORDER BY [Id]) AS [Vaga]
      FROM [dbo].[RecursosProvisionados] WHERE [Estado] <> N'removido') n ON n.[Id] = r.[Id]
WHERE r.[Vaga] = 0;
GO

-- A COTA atômica: uma vaga por (usuário, tipo) entre os não removidos. Dois pedidos simultâneos não ocupam a mesma.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecursosProvisionados_UsuarioId_Tipo_Vaga' AND object_id = OBJECT_ID(N'[dbo].[RecursosProvisionados]'))
    CREATE UNIQUE INDEX [IX_RecursosProvisionados_UsuarioId_Tipo_Vaga] ON [dbo].[RecursosProvisionados] ([UsuarioId], [Tipo], [Vaga]) WHERE [Estado] <> N'removido';
GO

-- A fila do worker: os pedidos por estado e pela próxima tentativa.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecursosProvisionados_Estado_ProximaTentativaEmUtc' AND object_id = OBJECT_ID(N'[dbo].[RecursosProvisionados]'))
    CREATE INDEX [IX_RecursosProvisionados_Estado_ProximaTentativaEmUtc] ON [dbo].[RecursosProvisionados] ([Estado], [ProximaTentativaEmUtc]);
GO

IF OBJECT_ID(N'[dbo].[EventosDaProvisao]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EventosDaProvisao] (
        [Id] bigint NOT NULL IDENTITY,
        [RecursoId] bigint NULL,
        [UsuarioId] int NOT NULL,
        [Evento] nvarchar(30) NOT NULL,
        [Autor] nvarchar(100) NOT NULL,
        [Detalhe] nvarchar(500) NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_EventosDaProvisao] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventosDaProvisao_RecursosProvisionados_RecursoId] FOREIGN KEY ([RecursoId]) REFERENCES [dbo].[RecursosProvisionados] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventosDaProvisao_RecursoId_EmUtc' AND object_id = OBJECT_ID(N'[dbo].[EventosDaProvisao]'))
    CREATE INDEX [IX_EventosDaProvisao_RecursoId_EmUtc] ON [dbo].[EventosDaProvisao] ([RecursoId], [EmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventosDaProvisao_UsuarioId_EmUtc' AND object_id = OBJECT_ID(N'[dbo].[EventosDaProvisao]'))
    CREATE INDEX [IX_EventosDaProvisao_UsuarioId_EmUtc] ON [dbo].[EventosDaProvisao] ([UsuarioId], [EmUtc]);
GO
