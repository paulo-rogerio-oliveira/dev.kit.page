-- Matrix v1. Aditivo e idempotente; aplicar antes da API que anuncia matrix-v1.
IF OBJECT_ID(N'[dbo].[RevisoesMatrix]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RevisoesMatrix] (
        [Id] bigint NOT NULL IDENTITY,
        [Espaco] nvarchar(80) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Aplicacao] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Tipo] nvarchar(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [DocumentoId] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Versao] int NOT NULL,
        [DonoId] int NOT NULL,
        [ProjetoId] bigint NULL,
        [AutorId] int NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        [Documento] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_RevisoesMatrix] PRIMARY KEY ([Id])
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RevisoesMatrix') AND name=N'IX_RevisoesMatrix_Espaco_Aplicacao_Tipo_DocumentoId_Versao')
    CREATE UNIQUE INDEX IX_RevisoesMatrix_Espaco_Aplicacao_Tipo_DocumentoId_Versao
        ON dbo.RevisoesMatrix(Espaco, Aplicacao, Tipo, DocumentoId, Versao);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RevisoesMatrix') AND name=N'IX_RevisoesMatrix_ProjetoId')
    CREATE INDEX IX_RevisoesMatrix_ProjetoId ON dbo.RevisoesMatrix(ProjetoId);
