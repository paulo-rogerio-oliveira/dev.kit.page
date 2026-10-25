-- A US #687 no Azure SQL: o admin orientado a utilidade.
--   * Usuarios ganha a licença definida pelo admin (validade, sem prazo, tipo, revogação e a última alteração) e
--     HistoricosDeLicenca guarda cada ação com o motivo — a renovação do dev.kit respeita o que fica aqui.
--   * Empresas ganha os dados básicos (CNPJ, segmento, cidade/UF, contato, telefone, site), o contrato até e quem editou.
--   * FotosDeEstado guarda o último estado de cada task, pergunta, fechamento, amostra de máquina e sugestão de impacto
--     (o "Saúde agora" e o "Onde as tasks travam" do painel).
--   * HistoricosDeItemDoCatalogo e AnexosDeItemDoCatalogo: o histórico e os anexos da edição de artefato do catálogo.
--
-- Só acrescenta: o que já existe fica sem validade definida (a renovação segue como antes) e sem dados novos. É
-- idempotente (pode rodar de novo sem efeito) e espelha a migration Us687AdminDeUtilidade do SQLite. Rode-o ANTES de
-- publicar a versão da API com as rotas /api/usuarios/{id}/licenca, PUT /api/empresas/{id}, /api/dashboard/utilidade e
-- /api/catalogos/itens (ver docs/publicacao-azure.md).

IF COL_LENGTH(N'[dbo].[Usuarios]', N'LicencaValidaAteUtc') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [LicencaValidaAteUtc] datetime2 NULL;
GO

IF COL_LENGTH(N'[dbo].[Usuarios]', N'LicencaSemPrazo') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [LicencaSemPrazo] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

IF COL_LENGTH(N'[dbo].[Usuarios]', N'LicencaTipo') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [LicencaTipo] nvarchar(20) NOT NULL DEFAULT N'';
GO

IF COL_LENGTH(N'[dbo].[Usuarios]', N'LicencaRevogadaEmUtc') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [LicencaRevogadaEmUtc] datetime2 NULL;
GO

IF COL_LENGTH(N'[dbo].[Usuarios]', N'LicencaAlteradaEmUtc') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [LicencaAlteradaEmUtc] datetime2 NULL;
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'Cnpj') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [Cnpj] nvarchar(14) NOT NULL DEFAULT N'';
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'Segmento') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [Segmento] nvarchar(60) NOT NULL DEFAULT N'';
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'Cidade') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [Cidade] nvarchar(80) NOT NULL DEFAULT N'';
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'Uf') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [Uf] nvarchar(2) NOT NULL DEFAULT N'';
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'Contato') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [Contato] nvarchar(120) NOT NULL DEFAULT N'';
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'Telefone') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [Telefone] nvarchar(30) NOT NULL DEFAULT N'';
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'Site') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [Site] nvarchar(200) NOT NULL DEFAULT N'';
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'ContratoAteUtc') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [ContratoAteUtc] datetime2 NULL;
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'AlteradaEmUtc') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [AlteradaEmUtc] datetime2 NULL;
GO

IF COL_LENGTH(N'[dbo].[Empresas]', N'AlteradaPor') IS NULL
    ALTER TABLE [dbo].[Empresas] ADD [AlteradaPor] nvarchar(100) NOT NULL DEFAULT N'';
GO

IF OBJECT_ID(N'[dbo].[HistoricosDeLicenca]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[HistoricosDeLicenca] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [Acao] nvarchar(20) NOT NULL,
        [Descricao] nvarchar(200) NOT NULL,
        [Motivo] nvarchar(300) NOT NULL,
        [ValidaAteUtc] datetime2 NULL,
        [Autor] nvarchar(100) NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_HistoricosDeLicenca] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_HistoricosDeLicenca_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_HistoricosDeLicenca_UsuarioId_EmUtc] ON [dbo].[HistoricosDeLicenca] ([UsuarioId], [EmUtc]);
END
GO

IF OBJECT_ID(N'[dbo].[FotosDeEstado]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FotosDeEstado] (
        [Id] bigint NOT NULL IDENTITY,
        [MaquinaId] int NOT NULL,
        [Tipo] nvarchar(50) NOT NULL,
        [Chave] nvarchar(200) NOT NULL,
        [Detalhe] nvarchar(200) NOT NULL,
        [Valor] bigint NULL,
        [EmUtc] datetime2 NOT NULL,
        [EventId] nvarchar(64) NOT NULL,
        CONSTRAINT [PK_FotosDeEstado] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FotosDeEstado_Maquinas_MaquinaId] FOREIGN KEY ([MaquinaId]) REFERENCES [dbo].[Maquinas] ([Id]) ON DELETE CASCADE
    );
    -- UMA foto por (máquina, tipo, chave): é o que garante que o upsert da ingestão nunca deixa duas.
    CREATE UNIQUE INDEX [IX_FotosDeEstado_MaquinaId_Tipo_Chave] ON [dbo].[FotosDeEstado] ([MaquinaId], [Tipo], [Chave]);
    CREATE INDEX [IX_FotosDeEstado_Tipo_EmUtc] ON [dbo].[FotosDeEstado] ([Tipo], [EmUtc]);
END
GO

IF OBJECT_ID(N'[dbo].[HistoricosDeItemDoCatalogo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[HistoricosDeItemDoCatalogo] (
        [Id] bigint NOT NULL IDENTITY,
        [ItemId] bigint NOT NULL,
        [Acao] nvarchar(20) NOT NULL,
        [Versao] int NOT NULL,
        [Autor] nvarchar(100) NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_HistoricosDeItemDoCatalogo] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_HistoricosDeItemDoCatalogo_ItensEditaveisDeCatalogo_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[ItensEditaveisDeCatalogo] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_HistoricosDeItemDoCatalogo_ItemId_EmUtc] ON [dbo].[HistoricosDeItemDoCatalogo] ([ItemId], [EmUtc]);
END
GO

IF OBJECT_ID(N'[dbo].[AnexosDeItemDoCatalogo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AnexosDeItemDoCatalogo] (
        [Id] bigint NOT NULL IDENTITY,
        [ItemId] bigint NOT NULL,
        [Nome] nvarchar(200) NOT NULL,
        [TipoDeConteudo] nvarchar(100) NOT NULL,
        [TamanhoBytes] bigint NOT NULL,
        [Conteudo] varbinary(max) NOT NULL,
        [Autor] nvarchar(100) NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AnexosDeItemDoCatalogo] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AnexosDeItemDoCatalogo_ItensEditaveisDeCatalogo_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[ItensEditaveisDeCatalogo] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_AnexosDeItemDoCatalogo_ItemId] ON [dbo].[AnexosDeItemDoCatalogo] ([ItemId]);
END
GO
