-- Tabela do ROI por work item (US #387) no Azure SQL: a FOTO mais recente de cada (máquina, work item)
-- que o dev.kit calculou e mandou no evento RoiCalculado.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema
-- só numa base VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente
-- (pode rodar de novo sem efeito) e espelha a migration 20261008031713_RoiDeWorkItem do SQLite.
-- Rode-o ANTES de publicar a versão da API que tem o painel de ROI (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[RoisDeWorkItem]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RoisDeWorkItem] (
        [Id] bigint NOT NULL IDENTITY,
        [MaquinaId] int NOT NULL,
        [WorkItemId] int NOT NULL,
        [Tipo] nvarchar(50) NOT NULL,
        [Estado] nvarchar(50) NOT NULL,
        [De] date NOT NULL,
        [Ate] date NOT NULL,
        [TurnosDoAgente] int NOT NULL,
        [Sessoes] int NOT NULL,
        [Horas] decimal(10,2) NOT NULL,
        [HorasNoBoard] decimal(10,2) NOT NULL,
        [HorasNoTimesheet] decimal(10,2) NULL,
        [LeadTimeDias] float NULL,
        [Aberto] bit NOT NULL,
        [PullRequests] int NOT NULL,
        [PullRequestsMergeadas] int NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        [EventId] nvarchar(64) NOT NULL,
        CONSTRAINT [PK_RoisDeWorkItem] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RoisDeWorkItem_Maquinas_MaquinaId] FOREIGN KEY ([MaquinaId]) REFERENCES [dbo].[Maquinas] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RoisDeWorkItem_MaquinaId_WorkItemId' AND object_id = OBJECT_ID(N'[dbo].[RoisDeWorkItem]'))
    CREATE UNIQUE INDEX [IX_RoisDeWorkItem_MaquinaId_WorkItemId] ON [dbo].[RoisDeWorkItem] ([MaquinaId], [WorkItemId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RoisDeWorkItem_EmUtc' AND object_id = OBJECT_ID(N'[dbo].[RoisDeWorkItem]'))
    CREATE INDEX [IX_RoisDeWorkItem_EmUtc] ON [dbo].[RoisDeWorkItem] ([EmUtc]);
GO
