-- A conversa e os comandos remotos (controle remoto, fase 2), no Azure SQL: o comando do site com o estado e o TTL, a
-- resposta do dev.kit, a fala do chat do dev.kit e o evento do SSE.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base
-- VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito)
-- e espelha a migration ConversaRemota do SQLite. Rode-o DEPOIS de Remoto.sql e Provisao.sql e ANTES de publicar a
-- versão da API com as rotas /api/remoto/comandos (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[ComandosRemotos]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ComandosRemotos] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [ComandoId] nvarchar(64) NOT NULL,
        [MaquinaId] nvarchar(64) NOT NULL,
        [Tipo] nvarchar(20) NOT NULL,
        [Texto] nvarchar(4000) NOT NULL,
        [Dispositivo] nvarchar(20) NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        [RecursoId] bigint NULL,
        [Erro] nvarchar(500) NOT NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        [ExpiraEmUtc] datetime2 NOT NULL,
        [EntregueEmUtc] datetime2 NULL,
        [ConcluidoEmUtc] datetime2 NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ComandosRemotos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ComandosRemotos_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- O id viaja na fila: único por usuário, e o reenvio com ele devolve o mesmo comando.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ComandosRemotos_UsuarioId_ComandoId' AND object_id = OBJECT_ID(N'[dbo].[ComandosRemotos]'))
    CREATE UNIQUE INDEX [IX_ComandosRemotos_UsuarioId_ComandoId] ON [dbo].[ComandosRemotos] ([UsuarioId], [ComandoId]);
GO

-- O histórico por máquina e o limite de comandos por hora.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ComandosRemotos_UsuarioId_MaquinaId_CriadoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[ComandosRemotos]'))
    CREATE INDEX [IX_ComandosRemotos_UsuarioId_MaquinaId_CriadoEmUtc] ON [dbo].[ComandosRemotos] ([UsuarioId], [MaquinaId], [CriadoEmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ComandosRemotos_UsuarioId_CriadoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[ComandosRemotos]'))
    CREATE INDEX [IX_ComandosRemotos_UsuarioId_CriadoEmUtc] ON [dbo].[ComandosRemotos] ([UsuarioId], [CriadoEmUtc]);
GO

-- O TTL: os que aguardam a entrega, pelo fim dele.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ComandosRemotos_Estado_ExpiraEmUtc' AND object_id = OBJECT_ID(N'[dbo].[ComandosRemotos]'))
    CREATE INDEX [IX_ComandosRemotos_Estado_ExpiraEmUtc] ON [dbo].[ComandosRemotos] ([Estado], [ExpiraEmUtc]);
GO

IF OBJECT_ID(N'[dbo].[RespostasRemotas]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RespostasRemotas] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [MaquinaId] nvarchar(64) NOT NULL,
        [RespostaId] nvarchar(64) NOT NULL,
        [ComandoId] nvarchar(64) NOT NULL,
        [Tipo] nvarchar(20) NOT NULL,
        [Texto] nvarchar(4000) NOT NULL,
        [GeradaEmUtc] datetime2 NOT NULL,
        [RecebidaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_RespostasRemotas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RespostasRemotas_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- O reenvio do dev.kit não duplica a resposta.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RespostasRemotas_UsuarioId_MaquinaId_RespostaId' AND object_id = OBJECT_ID(N'[dbo].[RespostasRemotas]'))
    CREATE UNIQUE INDEX [IX_RespostasRemotas_UsuarioId_MaquinaId_RespostaId] ON [dbo].[RespostasRemotas] ([UsuarioId], [MaquinaId], [RespostaId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RespostasRemotas_UsuarioId_MaquinaId_RecebidaEmUtc' AND object_id = OBJECT_ID(N'[dbo].[RespostasRemotas]'))
    CREATE INDEX [IX_RespostasRemotas_UsuarioId_MaquinaId_RecebidaEmUtc] ON [dbo].[RespostasRemotas] ([UsuarioId], [MaquinaId], [RecebidaEmUtc]);
GO

-- A retenção (Remoto:RetencaoDaConversaDias) apaga pela data.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RespostasRemotas_RecebidaEmUtc' AND object_id = OBJECT_ID(N'[dbo].[RespostasRemotas]'))
    CREATE INDEX [IX_RespostasRemotas_RecebidaEmUtc] ON [dbo].[RespostasRemotas] ([RecebidaEmUtc]);
GO

IF OBJECT_ID(N'[dbo].[EventosDaConversa]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EventosDaConversa] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [MaquinaId] nvarchar(64) NOT NULL,
        [Tipo] nvarchar(20) NOT NULL,
        [Conteudo] nvarchar(max) NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_EventosDaConversa] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventosDaConversa_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- O SSE lê os do usuário depois do último id visto.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventosDaConversa_UsuarioId_Id' AND object_id = OBJECT_ID(N'[dbo].[EventosDaConversa]'))
    CREATE INDEX [IX_EventosDaConversa_UsuarioId_Id] ON [dbo].[EventosDaConversa] ([UsuarioId], [Id]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventosDaConversa_EmUtc' AND object_id = OBJECT_ID(N'[dbo].[EventosDaConversa]'))
    CREATE INDEX [IX_EventosDaConversa_EmUtc] ON [dbo].[EventosDaConversa] ([EmUtc]);
GO

-- As falas do chat do dev.kit (item 13 do controle-remoto-v2), com o autor e a origem: espelha a migration FalasDoDesktop.
-- Rode de novo numa base que já tem as tabelas acima: só esta é criada.
IF OBJECT_ID(N'[dbo].[FalasRemotas]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FalasRemotas] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [MaquinaId] nvarchar(64) NOT NULL,
        [FalaId] nvarchar(64) NOT NULL,
        [Autor] nvarchar(20) NOT NULL,
        [Origem] nvarchar(20) NOT NULL,
        [Texto] nvarchar(4000) NOT NULL,
        [ComandoId] nvarchar(64) NOT NULL,
        [DitaEmUtc] datetime2 NOT NULL,
        [RecebidaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_FalasRemotas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FalasRemotas_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- O reenvio do dev.kit não duplica a fala.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FalasRemotas_UsuarioId_MaquinaId_FalaId' AND object_id = OBJECT_ID(N'[dbo].[FalasRemotas]'))
    CREATE UNIQUE INDEX [IX_FalasRemotas_UsuarioId_MaquinaId_FalaId] ON [dbo].[FalasRemotas] ([UsuarioId], [MaquinaId], [FalaId]);
GO

-- O histórico por máquina, na ordem em que foram ditas.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FalasRemotas_UsuarioId_MaquinaId_DitaEmUtc' AND object_id = OBJECT_ID(N'[dbo].[FalasRemotas]'))
    CREATE INDEX [IX_FalasRemotas_UsuarioId_MaquinaId_DitaEmUtc] ON [dbo].[FalasRemotas] ([UsuarioId], [MaquinaId], [DitaEmUtc]);
GO

-- A retenção (Remoto:RetencaoDaConversaDias) apaga pela data.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FalasRemotas_RecebidaEmUtc' AND object_id = OBJECT_ID(N'[dbo].[FalasRemotas]'))
    CREATE INDEX [IX_FalasRemotas_RecebidaEmUtc] ON [dbo].[FalasRemotas] ([RecebidaEmUtc]);
GO
