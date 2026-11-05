-- Aplicar antes da publicação da fase 3 em base existente. Não altera dados de produção por si só.
IF OBJECT_ID(N'[dbo].[Autenticadores]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Autenticadores] (
        [UsuarioId] int NOT NULL PRIMARY KEY,
        [SegredoProtegido] nvarchar(1000) NOT NULL,
        [Ativo] bit NOT NULL,
        [PreparadoEmUtc] datetime2 NOT NULL,
        [UltimoPasso] bigint NOT NULL,
        [Tentativas] int NOT NULL,
        [JanelaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [FK_Autenticadores_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO
IF OBJECT_ID(N'[dbo].[RespostasDasDecisoes]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RespostasDasDecisoes] (
        [Id] bigint NOT NULL IDENTITY PRIMARY KEY,
        [UsuarioId] int NOT NULL,
        [MaquinaId] nvarchar(64) NOT NULL,
        [PendenciaId] nvarchar(64) NOT NULL,
        [SnapshotId] nvarchar(64) NOT NULL,
        [Acao] nvarchar(20) NOT NULL,
        [Opcao] nvarchar(200) NULL,
        [Origem] nvarchar(300) NULL,
        [Destino] nvarchar(300) NULL,
        [Sha] nvarchar(40) NULL,
        [EmUtc] datetime2 NOT NULL,
        [SegundoFator] bit NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        CONSTRAINT [FK_RespostasDasDecisoes_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RespostasDasDecisoes_UsuarioId_MaquinaId_PendenciaId_SnapshotId' AND object_id = OBJECT_ID(N'[dbo].[RespostasDasDecisoes]'))
    CREATE UNIQUE INDEX [IX_RespostasDasDecisoes_UsuarioId_MaquinaId_PendenciaId_SnapshotId] ON [dbo].[RespostasDasDecisoes] ([UsuarioId], [MaquinaId], [PendenciaId], [SnapshotId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RespostasDasDecisoes_UsuarioId_MaquinaId_Estado' AND object_id = OBJECT_ID(N'[dbo].[RespostasDasDecisoes]'))
    CREATE INDEX [IX_RespostasDasDecisoes_UsuarioId_MaquinaId_Estado] ON [dbo].[RespostasDasDecisoes] ([UsuarioId], [MaquinaId], [Estado]);
GO
