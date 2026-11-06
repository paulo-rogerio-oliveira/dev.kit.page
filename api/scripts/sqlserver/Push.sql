-- Item 11: executar pelo processo de publicação, somente após revisão. A API não executa este script.
IF OBJECT_ID(N'[dbo].[AssinaturasDoPush]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AssinaturasDoPush] (
        [Id] nvarchar(32) NOT NULL PRIMARY KEY,
        [UsuarioId] int NOT NULL REFERENCES [dbo].[Usuarios]([Id]) ON DELETE CASCADE,
        [EndpointHash] nvarchar(64) NOT NULL,
        [DadosProtegidos] nvarchar(max) NOT NULL,
        [ChavePublicaServidor] nvarchar(87) NOT NULL,
        [AtualizadaEmUtc] datetime2 NOT NULL,
        [VerificadaEmUtc] datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX [IX_AssinaturasDoPush_EndpointHash] ON [dbo].[AssinaturasDoPush]([EndpointHash]);
    CREATE INDEX [IX_AssinaturasDoPush_UsuarioId] ON [dbo].[AssinaturasDoPush]([UsuarioId]);
    CREATE INDEX [IX_AssinaturasDoPush_VerificadaEmUtc] ON [dbo].[AssinaturasDoPush]([VerificadaEmUtc]);
    CREATE INDEX [IX_AssinaturasDoPush_AtualizadaEmUtc] ON [dbo].[AssinaturasDoPush]([AtualizadaEmUtc]);
END;
IF OBJECT_ID(N'[dbo].[EntregasDoPush]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EntregasDoPush] (
        [Id] nvarchar(32) NOT NULL PRIMARY KEY,
        [AssinaturaId] nvarchar(32) NOT NULL REFERENCES [dbo].[AssinaturasDoPush]([Id]) ON DELETE CASCADE,
        [MaquinaId] nvarchar(64) NOT NULL,
        [PendenciaId] nvarchar(64) NOT NULL,
        [SnapshotId] nvarchar(64) NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        [Reserva] nvarchar(32) NOT NULL,
        [Tentativas] int NOT NULL,
        [CriadaEmUtc] datetime2 NOT NULL,
        [ExpiraEmUtc] datetime2 NOT NULL,
        [ProximaTentativaUtc] datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX [IX_EntregasDoPush_AssinaturaId_MaquinaId_PendenciaId_SnapshotId]
        ON [dbo].[EntregasDoPush]([AssinaturaId], [MaquinaId], [PendenciaId], [SnapshotId]);
    CREATE INDEX [IX_EntregasDoPush_Estado_ProximaTentativaUtc] ON [dbo].[EntregasDoPush]([Estado], [ProximaTentativaUtc]);
    CREATE INDEX [IX_EntregasDoPush_CriadaEmUtc] ON [dbo].[EntregasDoPush]([CriadaEmUtc]);
END;
