-- A US #602 no Azure SQL: os RELATOS dos usuários do dev.kit (defeito, sugestão, dúvida), ligados ao usuário e à
-- empresa, com o estado e a resposta de quem administra.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito)
-- e espelha a migration Us602Relatos do SQLite. Rode-o ANTES de publicar a versão da API com as rotas /api/relatos
-- (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[Relatos]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Relatos] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [EmpresaId] int NULL,
        [Categoria] nvarchar(20) NOT NULL,
        [Texto] nvarchar(4000) NOT NULL,
        [Contexto] nvarchar(max) NOT NULL,
        [Captura] varbinary(max) NULL,
        [VersaoDevKit] nvarchar(50) NOT NULL,
        [ChaveDoCliente] nvarchar(64) NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        [Resposta] nvarchar(4000) NOT NULL,
        [RespondidoPorId] int NULL,
        [RespondidoEmUtc] datetime2 NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Relatos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Relatos_Empresas_EmpresaId] FOREIGN KEY ([EmpresaId]) REFERENCES [dbo].[Empresas] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Relatos_Usuarios_RespondidoPorId] FOREIGN KEY ([RespondidoPorId]) REFERENCES [dbo].[Usuarios] ([Id]),
        CONSTRAINT [FK_Relatos_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Relatos_CriadoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[Relatos]'))
    CREATE INDEX [IX_Relatos_CriadoEmUtc] ON [dbo].[Relatos] ([CriadoEmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Relatos_EmpresaId_CriadoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[Relatos]'))
    CREATE INDEX [IX_Relatos_EmpresaId_CriadoEmUtc] ON [dbo].[Relatos] ([EmpresaId], [CriadoEmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Relatos_RespondidoPorId' AND object_id = OBJECT_ID(N'[dbo].[Relatos]'))
    CREATE INDEX [IX_Relatos_RespondidoPorId] ON [dbo].[Relatos] ([RespondidoPorId]);
GO

-- A chave do cliente é única por usuário só quando veio: o reenvio da fila offline do dev.kit devolve o mesmo relato.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Relatos_UsuarioId_ChaveDoCliente' AND object_id = OBJECT_ID(N'[dbo].[Relatos]'))
    CREATE UNIQUE INDEX [IX_Relatos_UsuarioId_ChaveDoCliente] ON [dbo].[Relatos] ([UsuarioId], [ChaveDoCliente]) WHERE [ChaveDoCliente] <> '';
GO
