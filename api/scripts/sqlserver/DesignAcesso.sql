-- O ACESSO aos projetos de design no Azure SQL: o dono do projeto (ProjetosDeDesign.DonoId) e os acessos que ele concede
-- (leitor ou editor). Idempotente, espelha a migration 20261105060000_DesignAcesso do SQLite. Rode-o ANTES de publicar a
-- versão da API com as rotas /api/design/projetos/{id}/acessos (ver docs/publicacao-azure.md): sem ele, todo o design falha.
--
-- A carga dos projetos que já existem: o dono reconhecido é o do nível usuário, ou quem enviou a primeira versão; quem já
-- tinha enviado versões fica editor, para ninguém perder o envio. Rodar de novo não duplica (o NOT EXISTS e o DonoId = 0).

IF COL_LENGTH(N'dbo.ProjetosDeDesign', N'DonoId') IS NULL
    ALTER TABLE [dbo].[ProjetosDeDesign] ADD [DonoId] int NOT NULL CONSTRAINT [DF_ProjetosDeDesign_DonoId] DEFAULT 0;
GO

IF OBJECT_ID(N'[dbo].[AcessosAoDesign]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AcessosAoDesign] (
        [Id] bigint NOT NULL IDENTITY,
        [ProjetoId] bigint NOT NULL,
        [UsuarioId] int NOT NULL,
        [Papel] nvarchar(10) NOT NULL,
        [ConcedidoPorId] int NOT NULL,
        [ConcedidoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AcessosAoDesign] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AcessosAoDesign_ProjetosDeDesign_ProjetoId] FOREIGN KEY ([ProjetoId]) REFERENCES [dbo].[ProjetosDeDesign] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AcessosAoDesign_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id])
    );
END;
GO

-- Um acesso por usuário em cada projeto.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcessosAoDesign_ProjetoId_UsuarioId' AND object_id = OBJECT_ID(N'[dbo].[AcessosAoDesign]'))
    CREATE UNIQUE INDEX [IX_AcessosAoDesign_ProjetoId_UsuarioId] ON [dbo].[AcessosAoDesign] ([ProjetoId], [UsuarioId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcessosAoDesign_UsuarioId' AND object_id = OBJECT_ID(N'[dbo].[AcessosAoDesign]'))
    CREATE INDEX [IX_AcessosAoDesign_UsuarioId] ON [dbo].[AcessosAoDesign] ([UsuarioId]);
GO

-- O dono de cada projeto existente.
UPDATE p SET [DonoId] = CASE
    WHEN p.[Nivel] = N'usuario' AND p.[UsuarioId] > 0 THEN p.[UsuarioId]
    ELSE COALESCE((SELECT TOP (1) v.[AutorId] FROM [dbo].[VersoesDeDesign] v INNER JOIN [dbo].[TelasDeDesign] t ON t.[Id] = v.[TelaId]
        WHERE t.[ProjetoId] = p.[Id] ORDER BY v.[EnviadaEmUtc], v.[Id]), 0)
END
FROM [dbo].[ProjetosDeDesign] p
WHERE p.[DonoId] = 0;
GO

-- Quem já enviou versões, sem ser o dono, fica editor.
INSERT INTO [dbo].[AcessosAoDesign] ([ProjetoId], [UsuarioId], [Papel], [ConcedidoPorId], [ConcedidoEmUtc])
SELECT DISTINCT t.[ProjetoId], v.[AutorId], N'editor', 0, SYSUTCDATETIME()
FROM [dbo].[VersoesDeDesign] v
INNER JOIN [dbo].[TelasDeDesign] t ON t.[Id] = v.[TelaId]
INNER JOIN [dbo].[ProjetosDeDesign] p ON p.[Id] = t.[ProjetoId]
WHERE v.[AutorId] <> p.[DonoId]
  AND NOT EXISTS (SELECT 1 FROM [dbo].[AcessosAoDesign] a WHERE a.[ProjetoId] = t.[ProjetoId] AND a.[UsuarioId] = v.[AutorId]);
GO
