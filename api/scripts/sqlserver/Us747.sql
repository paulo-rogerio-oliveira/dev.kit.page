-- Executar antes de publicar a API. Espelha Us747ProjetosCompartilhados, sem modificar dados existentes.
IF OBJECT_ID(N'[dbo].[ProjetosCompartilhados]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ProjetosCompartilhados] (
        [Id] bigint NOT NULL IDENTITY,
        [Espaco] nvarchar(60) NOT NULL,
        [EmpresaId] int NULL,
        [UsuarioId] int NOT NULL,
        [Nivel] nvarchar(10) NOT NULL,
        [Time] nvarchar(100) NOT NULL,
        [Slug] nvarchar(96) NOT NULL,
        [Autor] nvarchar(200) NOT NULL,
        [Versao] int NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        [Documento] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_ProjetosCompartilhados] PRIMARY KEY ([Id])
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjetosCompartilhados_Espaco_Nivel_Time_UsuarioId_Slug' AND object_id = OBJECT_ID(N'[dbo].[ProjetosCompartilhados]'))
    CREATE UNIQUE INDEX [IX_ProjetosCompartilhados_Espaco_Nivel_Time_UsuarioId_Slug]
    ON [dbo].[ProjetosCompartilhados] ([Espaco], [Nivel], [Time], [UsuarioId], [Slug]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjetosCompartilhados_AtualizadoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[ProjetosCompartilhados]'))
    CREATE INDEX [IX_ProjetosCompartilhados_AtualizadoEmUtc] ON [dbo].[ProjetosCompartilhados] ([AtualizadoEmUtc]);
GO
