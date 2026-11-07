-- O handshake do chat remoto (remote-control-v3, item 1) no Azure SQL: o último catálogo de cada máquina do dev.kit, por
-- usuário — os work items, repositórios, fluxos, projetos e designs que os atalhos do chat do Board mostram.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base VAZIA —
-- numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito) e espelha a
-- migration CatalogoRemoto do SQLite. Rode-o ANTES de publicar a versão da API com PUT /api/remoto/catalogo (ver
-- docs/publicacao-azure.md); sem ele, a verificação do esquema na subida aponta a tabela e este script.

IF OBJECT_ID(N'[dbo].[CatalogosRemotos]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CatalogosRemotos] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [MaquinaId] nvarchar(64) NOT NULL,
        [Catalogo] nvarchar(max) NOT NULL,
        [GeradoEmUtc] datetime2 NOT NULL,
        [RecebidoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_CatalogosRemotos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CatalogosRemotos_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- O isolamento: um catálogo por (usuário, máquina), e nenhum grava no do outro.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CatalogosRemotos_UsuarioId_MaquinaId' AND object_id = OBJECT_ID(N'[dbo].[CatalogosRemotos]'))
    CREATE UNIQUE INDEX [IX_CatalogosRemotos_UsuarioId_MaquinaId] ON [dbo].[CatalogosRemotos] ([UsuarioId], [MaquinaId]);
GO

-- A retenção (Remoto:RetencaoDias) apaga pelo último recebimento.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CatalogosRemotos_RecebidoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[CatalogosRemotos]'))
    CREATE INDEX [IX_CatalogosRemotos_RecebidoEmUtc] ON [dbo].[CatalogosRemotos] ([RecebidoEmUtc]);
GO
