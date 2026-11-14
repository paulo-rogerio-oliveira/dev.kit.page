-- (semana 7, 09/11/2026) A conversa com o agente de uma task pelo site, no Azure SQL: a task de cada fala (vazia na fala do
-- chat do Orquestrador) e o índice da conversa da task. O destino da mensagem vai no Contexto do comando, sem coluna.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base VAZIA —
-- numa base que já existe ele não cria coluna nem índice novo. Este script é idempotente (pode rodar de novo sem efeito) e
-- espelha a migration ConversaDaTask do SQLite. Rode-o ANTES de publicar a versão da API com o recurso "conversa-da-task";
-- sem ele, a verificação do esquema na subida aponta a coluna e este script.
--
-- Pré-checagem (regra #28): esta versão não traz configuração nova (appsettings) — nada a conferir além da tabela
-- FalasRemotas, que já existe desde o Conversa.sql.

IF OBJECT_ID(N'[dbo].[FalasRemotas]', N'U') IS NULL
    THROW 50001, N'Rode antes Conversa.sql: falta a tabela FalasRemotas.', 1;
GO

-- A task da fala (o id da execução no retrato); vazia é a fala do chat do Orquestrador.
IF COL_LENGTH(N'[dbo].[FalasRemotas]', N'Sessao') IS NULL
    ALTER TABLE [dbo].[FalasRemotas] ADD [Sessao] nvarchar(64) NOT NULL CONSTRAINT [DF_FalasRemotas_Sessao] DEFAULT N'';
GO

-- A conversa de uma task, na ordem em que as falas foram ditas.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FalasRemotas_UsuarioId_MaquinaId_Sessao_DitaEmUtc' AND object_id = OBJECT_ID(N'[dbo].[FalasRemotas]'))
    CREATE INDEX [IX_FalasRemotas_UsuarioId_MaquinaId_Sessao_DitaEmUtc] ON [dbo].[FalasRemotas] ([UsuarioId], [MaquinaId], [Sessao], [DitaEmUtc]);
GO
