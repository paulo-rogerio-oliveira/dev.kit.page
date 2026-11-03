-- O controle remoto, fase 1, no Azure SQL: o heartbeat e o retrato de cada máquina do dev.kit, por usuário.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito)
-- e espelha a migration RemotoHeartbeatERetrato do SQLite. Rode-o ANTES de publicar a versão da API com as rotas
-- /api/remoto (ver docs/publicacao-azure.md) — e ANTES do dev.kit que publica nelas.

IF OBJECT_ID(N'[dbo].[MaquinasRemotas]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MaquinasRemotas] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [MaquinaId] nvarchar(64) NOT NULL,
        [Nome] nvarchar(200) NOT NULL,
        [VersaoDevKit] nvarchar(50) NOT NULL,
        [Origem] nvarchar(20) NOT NULL,
        [AppAberto] bit NOT NULL,
        [EstadoDoAgente] nvarchar(20) NOT NULL,
        [TurnoEmAndamento] bit NOT NULL,
        [UltimoHeartbeatEmUtc] datetime2 NULL,
        [Retrato] nvarchar(max) NOT NULL DEFAULT N'',
        [RetratoGeradoEmUtc] datetime2 NULL,
        [RetratoRecebidoEmUtc] datetime2 NULL,
        [CriadaEmUtc] datetime2 NOT NULL,
        [AtualizadaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_MaquinasRemotas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MaquinasRemotas_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- O isolamento: o mesmo id anônimo de dois usuários são duas máquinas, e nenhum grava na do outro.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MaquinasRemotas_UsuarioId_MaquinaId' AND object_id = OBJECT_ID(N'[dbo].[MaquinasRemotas]'))
    CREATE UNIQUE INDEX [IX_MaquinasRemotas_UsuarioId_MaquinaId] ON [dbo].[MaquinasRemotas] ([UsuarioId], [MaquinaId]);
GO

-- A retenção (Remoto:RetencaoDias) apaga pelo último contato.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MaquinasRemotas_AtualizadaEmUtc' AND object_id = OBJECT_ID(N'[dbo].[MaquinasRemotas]'))
    CREATE INDEX [IX_MaquinasRemotas_AtualizadaEmUtc] ON [dbo].[MaquinasRemotas] ([AtualizadaEmUtc]);
GO
