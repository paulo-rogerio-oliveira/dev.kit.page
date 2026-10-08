-- Tabelas das exceções não classificadas (US #381) no Azure SQL: os grupos (um por assinatura,
-- com o estado da reação) e as ocorrências guardadas de cada um (com o trace sanitizado).
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema
-- só numa base VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente
-- (pode rodar de novo sem efeito) e espelha a migration 20261007215156_GruposDeErro do SQLite.
-- Rode-o ANTES de publicar a versão da API que tem o painel de exceções (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[GruposDeErro]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[GruposDeErro] (
        [Id] bigint NOT NULL IDENTITY,
        [Assinatura] nvarchar(64) NOT NULL,
        [Tipo] nvarchar(200) NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        [ResolvidoNaVersao] nvarchar(50) NULL,
        [PrimeiraVersao] nvarchar(50) NOT NULL,
        [UltimaVersao] nvarchar(50) NOT NULL,
        [PrimeiroVistoEmUtc] datetime2 NOT NULL,
        [UltimoVistoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_GruposDeErro] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GruposDeErro_Assinatura' AND object_id = OBJECT_ID(N'[dbo].[GruposDeErro]'))
    CREATE UNIQUE INDEX [IX_GruposDeErro_Assinatura] ON [dbo].[GruposDeErro] ([Assinatura]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GruposDeErro_UltimoVistoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[GruposDeErro]'))
    CREATE INDEX [IX_GruposDeErro_UltimoVistoEmUtc] ON [dbo].[GruposDeErro] ([UltimoVistoEmUtc]);
GO

IF OBJECT_ID(N'[dbo].[OcorrenciasDeErro]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OcorrenciasDeErro] (
        [Id] bigint NOT NULL IDENTITY,
        [GrupoId] bigint NOT NULL,
        [MaquinaId] int NOT NULL,
        [EventId] nvarchar(64) NOT NULL,
        [VersaoDevKit] nvarchar(50) NOT NULL,
        [Trace] nvarchar(max) NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_OcorrenciasDeErro] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OcorrenciasDeErro_GruposDeErro_GrupoId] FOREIGN KEY ([GrupoId]) REFERENCES [dbo].[GruposDeErro] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OcorrenciasDeErro_Maquinas_MaquinaId] FOREIGN KEY ([MaquinaId]) REFERENCES [dbo].[Maquinas] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OcorrenciasDeErro_EmUtc' AND object_id = OBJECT_ID(N'[dbo].[OcorrenciasDeErro]'))
    CREATE INDEX [IX_OcorrenciasDeErro_EmUtc] ON [dbo].[OcorrenciasDeErro] ([EmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OcorrenciasDeErro_GrupoId_EmUtc' AND object_id = OBJECT_ID(N'[dbo].[OcorrenciasDeErro]'))
    CREATE INDEX [IX_OcorrenciasDeErro_GrupoId_EmUtc] ON [dbo].[OcorrenciasDeErro] ([GrupoId], [EmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OcorrenciasDeErro_MaquinaId' AND object_id = OBJECT_ID(N'[dbo].[OcorrenciasDeErro]'))
    CREATE INDEX [IX_OcorrenciasDeErro_MaquinaId] ON [dbo].[OcorrenciasDeErro] ([MaquinaId]);
GO
