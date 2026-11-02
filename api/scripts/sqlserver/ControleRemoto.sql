-- O controle remoto no Azure SQL (projeto controle-remoto, fase 1): o último heartbeat de cada máquina, por dono, e o
-- último retrato dela (projetos, fluxos e pendências, sanitizados, em JSON).
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base VAZIA —
-- numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito) e espelha a
-- migration ControleRemoto do SQLite. Rode-o ANTES de publicar a versão da API com as rotas /api/remoto/v1 (ver
-- docs/publicacao-azure.md), e ANTES do pacote do dev.kit que as chama.

IF OBJECT_ID(N'[dbo].[MaquinasRemotas]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MaquinasRemotas] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [MaquinaId] nvarchar(32) NOT NULL,
        [Apelido] nvarchar(100) NOT NULL,
        [VersaoDevKit] nvarchar(50) NOT NULL,
        [DevKitAberto] bit NOT NULL,
        [Agente] nvarchar(30) NOT NULL,
        [TurnosEmAndamento] int NOT NULL,
        [UltimoHeartbeatEmUtc] datetime2 NOT NULL,
        [CriadaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_MaquinasRemotas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MaquinasRemotas_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID(N'[dbo].[RetratosRemotos]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RetratosRemotos] (
        [MaquinaRemotaId] bigint NOT NULL,
        [GeradoEmUtc] datetime2 NOT NULL,
        [RecebidoEmUtc] datetime2 NOT NULL,
        [Documento] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_RetratosRemotos] PRIMARY KEY ([MaquinaRemotaId]),
        CONSTRAINT [FK_RetratosRemotos_MaquinasRemotas_MaquinaRemotaId] FOREIGN KEY ([MaquinaRemotaId]) REFERENCES [dbo].[MaquinasRemotas] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- A máquina é única por (dono, id): o mesmo GUID em dois usuários são duas linhas que não se enxergam.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MaquinasRemotas_UsuarioId_MaquinaId' AND object_id = OBJECT_ID(N'[dbo].[MaquinasRemotas]'))
    CREATE UNIQUE INDEX [IX_MaquinasRemotas_UsuarioId_MaquinaId] ON [dbo].[MaquinasRemotas] ([UsuarioId], [MaquinaId]);
GO

-- O expurgo diário procura as máquinas paradas além da retenção.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MaquinasRemotas_UltimoHeartbeatEmUtc' AND object_id = OBJECT_ID(N'[dbo].[MaquinasRemotas]'))
    CREATE INDEX [IX_MaquinasRemotas_UltimoHeartbeatEmUtc] ON [dbo].[MaquinasRemotas] ([UltimoHeartbeatEmUtc]);
GO
