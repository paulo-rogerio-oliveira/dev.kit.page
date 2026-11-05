-- Após DesignAcesso.sql e antes da API nova. Preserva o dono dos projetos em ondas
-- e migra as concessões de design para a mesma tabela usada pelos dois recursos.
IF COL_LENGTH(N'dbo.ProjetosCompartilhados', N'DonoId') IS NULL
BEGIN
    ALTER TABLE [dbo].[ProjetosCompartilhados] ADD [DonoId] int NOT NULL CONSTRAINT [DF_ProjetosCompartilhados_DonoId] DEFAULT 0;
END;
GO
UPDATE [dbo].[ProjetosCompartilhados]
SET [DonoId] = CASE WHEN [Nivel] = N'usuario' AND [UsuarioId] > 0 THEN [UsuarioId] ELSE [AutorId] END
WHERE [DonoId] = 0;
GO
IF OBJECT_ID(N'[dbo].[AcessosAosProjetos]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AcessosAosProjetos] (
        [Id] bigint NOT NULL IDENTITY,
        [ProjetoId] bigint NULL,
        [ProjetoEmOndasId] bigint NULL,
        [UsuarioId] int NOT NULL,
        [Papel] nvarchar(10) NOT NULL,
        [ConcedidoPorId] int NOT NULL,
        [ConcedidoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AcessosAosProjetos] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AcessosAosProjetos_Recurso] CHECK (
            ([ProjetoId] IS NOT NULL AND [ProjetoEmOndasId] IS NULL) OR ([ProjetoId] IS NULL AND [ProjetoEmOndasId] IS NOT NULL)),
        CONSTRAINT [FK_AcessosAosProjetos_ProjetosDeDesign_ProjetoId] FOREIGN KEY ([ProjetoId]) REFERENCES [dbo].[ProjetosDeDesign] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AcessosAosProjetos_ProjetosCompartilhados_ProjetoEmOndasId] FOREIGN KEY ([ProjetoEmOndasId]) REFERENCES [dbo].[ProjetosCompartilhados] ([Id]),
        CONSTRAINT [FK_AcessosAosProjetos_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
    CREATE UNIQUE INDEX [IX_AcessosAosProjetos_ProjetoId_UsuarioId] ON [dbo].[AcessosAosProjetos] ([ProjetoId], [UsuarioId]) WHERE [ProjetoId] IS NOT NULL;
    CREATE UNIQUE INDEX [IX_AcessosAosProjetos_ProjetoEmOndasId_UsuarioId] ON [dbo].[AcessosAosProjetos] ([ProjetoEmOndasId], [UsuarioId]) WHERE [ProjetoEmOndasId] IS NOT NULL;
    CREATE INDEX [IX_AcessosAosProjetos_UsuarioId] ON [dbo].[AcessosAosProjetos] ([UsuarioId]);
END;
GO
-- O histórico antigo só é removido depois de copiar os acessos, numa transação.
IF OBJECT_ID(N'[dbo].[AcessosAoDesign]', N'U') IS NOT NULL
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    INSERT INTO [dbo].[AcessosAosProjetos] ([ProjetoId], [UsuarioId], [Papel], [ConcedidoPorId], [ConcedidoEmUtc])
    SELECT a.[ProjetoId], a.[UsuarioId], a.[Papel], a.[ConcedidoPorId], a.[ConcedidoEmUtc]
    FROM [dbo].[AcessosAoDesign] a
    WHERE NOT EXISTS (SELECT 1 FROM [dbo].[AcessosAosProjetos] n WHERE n.[ProjetoId] = a.[ProjetoId] AND n.[UsuarioId] = a.[UsuarioId]);
    DROP TABLE [dbo].[AcessosAoDesign];
    COMMIT;
END;
GO
