-- A US #556 no Azure SQL: as convenções da US #491 viram o CONTEXTO do dev.kit, SEM PERDA de dados. A tabela
-- ConvencoesDaEmpresa é RENOMEADA para Contextos (nada é apagado), o Time vira Equipe, cada item ganha a Origem (as
-- convenções existentes são instruções do usuário) e a Versao, o Valor passa a 4000 caracteres, e quem já gravou a
-- convenção de um time entra como participante APROVADO da equipe (ParticipantesDeEquipe).
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base VAZIA.
-- Este script é idempotente (pode rodar de novo sem efeito) e espelha a migration ContextoNoLugarDasConvencoes do
-- SQLite. Rode-o DEPOIS do Us491.sql e ANTES de publicar a versão da API com as rotas /api/contexto (ver
-- docs/publicacao-azure.md).

-- 1. A tabela: renomeada da das convenções; numa base que nunca teve a US #491, criada já no formato novo.
IF OBJECT_ID(N'[dbo].[Contextos]', N'U') IS NULL AND OBJECT_ID(N'[dbo].[ConvencoesDaEmpresa]', N'U') IS NOT NULL
    EXEC sp_rename N'[dbo].[ConvencoesDaEmpresa]', N'Contextos';
GO

IF OBJECT_ID(N'[dbo].[Contextos]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Contextos] (
        [Id] bigint NOT NULL IDENTITY,
        [EmpresaId] int NOT NULL,
        [Nivel] nvarchar(10) NOT NULL,
        [Equipe] nvarchar(80) NOT NULL,
        [UsuarioId] int NOT NULL,
        [Chave] nvarchar(60) NOT NULL,
        [Valor] nvarchar(4000) NOT NULL,
        [Origem] nvarchar(10) NOT NULL,
        [Versao] int NOT NULL,
        [AutorId] int NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Contextos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Contextos_Empresas_EmpresaId] FOREIGN KEY ([EmpresaId]) REFERENCES [dbo].[Empresas] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Contextos_Usuarios_AutorId] FOREIGN KEY ([AutorId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

-- 2. Os nomes das restrições da tabela renomeada acompanham o nome novo.
IF OBJECT_ID(N'[dbo].[PK_ConvencoesDaEmpresa]') IS NOT NULL
    EXEC sp_rename N'[dbo].[PK_ConvencoesDaEmpresa]', N'PK_Contextos', N'OBJECT';
GO

IF OBJECT_ID(N'[dbo].[FK_ConvencoesDaEmpresa_Empresas_EmpresaId]') IS NOT NULL
    EXEC sp_rename N'[dbo].[FK_ConvencoesDaEmpresa_Empresas_EmpresaId]', N'FK_Contextos_Empresas_EmpresaId', N'OBJECT';
GO

IF OBJECT_ID(N'[dbo].[FK_ConvencoesDaEmpresa_Usuarios_AutorId]') IS NOT NULL
    EXEC sp_rename N'[dbo].[FK_ConvencoesDaEmpresa_Usuarios_AutorId]', N'FK_Contextos_Usuarios_AutorId', N'OBJECT';
GO

-- 3. Os índices antigos saem (o único passa a incluir a origem).
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave' AND object_id = OBJECT_ID(N'[dbo].[Contextos]'))
    DROP INDEX [IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave] ON [dbo].[Contextos];
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_Chave' AND object_id = OBJECT_ID(N'[dbo].[Contextos]'))
    DROP INDEX [IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_Chave] ON [dbo].[Contextos];
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_AutorId' AND object_id = OBJECT_ID(N'[dbo].[Contextos]'))
    DROP INDEX [IX_ConvencoesDaEmpresa_AutorId] ON [dbo].[Contextos];
GO

-- 4. As colunas: Time → Equipe, a Origem (as convenções são instruções), a Versao e o Valor maior.
IF COL_LENGTH(N'[dbo].[Contextos]', N'Time') IS NOT NULL AND COL_LENGTH(N'[dbo].[Contextos]', N'Equipe') IS NULL
    EXEC sp_rename N'[dbo].[Contextos].[Time]', N'Equipe', N'COLUMN';
GO

IF COL_LENGTH(N'[dbo].[Contextos]', N'Origem') IS NULL
    ALTER TABLE [dbo].[Contextos] ADD [Origem] nvarchar(10) NOT NULL CONSTRAINT [DF_Contextos_Origem] DEFAULT N'usuario';
GO

IF COL_LENGTH(N'[dbo].[Contextos]', N'Versao') IS NULL
    ALTER TABLE [dbo].[Contextos] ADD [Versao] int NOT NULL CONSTRAINT [DF_Contextos_Versao] DEFAULT 1;
GO

-- COL_LENGTH devolve BYTES: nvarchar(4000) são 8000.
IF COL_LENGTH(N'[dbo].[Contextos]', N'Valor') < 8000
    ALTER TABLE [dbo].[Contextos] ALTER COLUMN [Valor] nvarchar(4000) NOT NULL;
GO

UPDATE [dbo].[Contextos] SET [Nivel] = N'equipe' WHERE [Nivel] = N'time';
GO

-- 5. Os índices novos.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contextos_EmpresaId_Nivel_Equipe_UsuarioId_Chave_Origem' AND object_id = OBJECT_ID(N'[dbo].[Contextos]'))
    CREATE UNIQUE INDEX [IX_Contextos_EmpresaId_Nivel_Equipe_UsuarioId_Chave_Origem] ON [dbo].[Contextos] ([EmpresaId], [Nivel], [Equipe], [UsuarioId], [Chave], [Origem]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contextos_AutorId' AND object_id = OBJECT_ID(N'[dbo].[Contextos]'))
    CREATE INDEX [IX_Contextos_AutorId] ON [dbo].[Contextos] ([AutorId]);
GO

-- 6. Quem participa de cada equipe: o dev pede, o gestor aprova.
IF OBJECT_ID(N'[dbo].[ParticipantesDeEquipe]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ParticipantesDeEquipe] (
        [Id] bigint NOT NULL IDENTITY,
        [EmpresaId] int NOT NULL,
        [UsuarioId] int NOT NULL,
        [Equipe] nvarchar(80) NOT NULL,
        [Situacao] nvarchar(10) NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ParticipantesDeEquipe] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ParticipantesDeEquipe_Empresas_EmpresaId] FOREIGN KEY ([EmpresaId]) REFERENCES [dbo].[Empresas] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ParticipantesDeEquipe_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ParticipantesDeEquipe_EmpresaId_UsuarioId_Equipe' AND object_id = OBJECT_ID(N'[dbo].[ParticipantesDeEquipe]'))
    CREATE UNIQUE INDEX [IX_ParticipantesDeEquipe_EmpresaId_UsuarioId_Equipe] ON [dbo].[ParticipantesDeEquipe] ([EmpresaId], [UsuarioId], [Equipe]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ParticipantesDeEquipe_EmpresaId_Equipe' AND object_id = OBJECT_ID(N'[dbo].[ParticipantesDeEquipe]'))
    CREATE INDEX [IX_ParticipantesDeEquipe_EmpresaId_Equipe] ON [dbo].[ParticipantesDeEquipe] ([EmpresaId], [Equipe]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ParticipantesDeEquipe_UsuarioId' AND object_id = OBJECT_ID(N'[dbo].[ParticipantesDeEquipe]'))
    CREATE INDEX [IX_ParticipantesDeEquipe_UsuarioId] ON [dbo].[ParticipantesDeEquipe] ([UsuarioId]);
GO

-- 7. Quem já gravou a convenção de um time participa dele, aprovado (rodar de novo não duplica).
INSERT INTO [dbo].[ParticipantesDeEquipe] ([EmpresaId], [UsuarioId], [Equipe], [Situacao], [AtualizadoEmUtc])
SELECT c.[EmpresaId], c.[AutorId], c.[Equipe], N'aprovado', MAX(c.[AtualizadoEmUtc])
FROM [dbo].[Contextos] c
WHERE c.[Nivel] = N'equipe' AND c.[Origem] = N'usuario'
  AND NOT EXISTS (SELECT 1 FROM [dbo].[ParticipantesDeEquipe] p
                  WHERE p.[EmpresaId] = c.[EmpresaId] AND p.[UsuarioId] = c.[AutorId] AND p.[Equipe] = c.[Equipe])
GROUP BY c.[EmpresaId], c.[AutorId], c.[Equipe];
GO
