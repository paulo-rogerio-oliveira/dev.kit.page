-- Tabela das avaliações de entrega (US #417) no Azure SQL: o joinha MAIS RECENTE de cada (máquina,
-- sessão, turno) que o dev.kit mandou no evento EntregaAvaliada, com o campo avaliacao.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema
-- só numa base VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente
-- (pode rodar de novo sem efeito) e espelha a migration 20261010144004_AvaliacoesDeEntrega do SQLite.
-- Rode-o ANTES de publicar a versão da API que tem as rotas de feedback (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[AvaliacoesDeEntrega]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AvaliacoesDeEntrega] (
        [Id] bigint NOT NULL IDENTITY,
        [MaquinaId] int NOT NULL,
        [SessaoId] nvarchar(100) NOT NULL,
        [Turno] int NOT NULL,
        [Boa] bit NOT NULL,
        [Motivo] nvarchar(200) NOT NULL,
        [Agente] nvarchar(50) NOT NULL,
        [Modelo] nvarchar(100) NOT NULL,
        [Fluxo] nvarchar(200) NOT NULL,
        [WorkItemId] int NULL,
        [EmUtc] datetime2 NOT NULL,
        [Dia] date NOT NULL,
        [EventId] nvarchar(64) NOT NULL,
        CONSTRAINT [PK_AvaliacoesDeEntrega] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AvaliacoesDeEntrega_Maquinas_MaquinaId] FOREIGN KEY ([MaquinaId]) REFERENCES [dbo].[Maquinas] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AvaliacoesDeEntrega_MaquinaId_SessaoId_Turno' AND object_id = OBJECT_ID(N'[dbo].[AvaliacoesDeEntrega]'))
    CREATE UNIQUE INDEX [IX_AvaliacoesDeEntrega_MaquinaId_SessaoId_Turno] ON [dbo].[AvaliacoesDeEntrega] ([MaquinaId], [SessaoId], [Turno]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AvaliacoesDeEntrega_EmUtc' AND object_id = OBJECT_ID(N'[dbo].[AvaliacoesDeEntrega]'))
    CREATE INDEX [IX_AvaliacoesDeEntrega_EmUtc] ON [dbo].[AvaliacoesDeEntrega] ([EmUtc]);
GO
