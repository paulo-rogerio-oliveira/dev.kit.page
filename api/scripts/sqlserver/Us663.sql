-- A US #663 no Azure SQL: a ORIGEM do contexto. ConvencoesDaEmpresa ganha Origem ('usuario', a regra que a pessoa
-- escreveu, ou 'agente', a sugestão que um agente do dev.kit registrou) e Versao (sobe a cada gravação; a promoção com a
-- versão velha é conflito). O índice único passa a incluir a origem: a regra e a sugestão do mesmo tema coexistem.
--
-- Só acrescenta: o que já existe vira Origem='usuario' e Versao=1 (tudo o que há hoje foi escrito por pessoa). É
-- idempotente (pode rodar de novo sem efeito) e espelha a migration Us663OrigemDoContexto do SQLite. Rode-o ANTES de
-- publicar a versão da API com as rotas /api/empresa/contexto/* (ver docs/publicacao-azure.md).

IF COL_LENGTH(N'[dbo].[ConvencoesDaEmpresa]', N'Origem') IS NULL
    ALTER TABLE [dbo].[ConvencoesDaEmpresa] ADD [Origem] nvarchar(10) NOT NULL DEFAULT N'usuario';
GO

IF COL_LENGTH(N'[dbo].[ConvencoesDaEmpresa]', N'Versao') IS NULL
    ALTER TABLE [dbo].[ConvencoesDaEmpresa] ADD [Versao] int NOT NULL DEFAULT 1;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave' AND object_id = OBJECT_ID(N'[dbo].[ConvencoesDaEmpresa]'))
    DROP INDEX [IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave] ON [dbo].[ConvencoesDaEmpresa];
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave_Origem' AND object_id = OBJECT_ID(N'[dbo].[ConvencoesDaEmpresa]'))
    CREATE UNIQUE INDEX [IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave_Origem] ON [dbo].[ConvencoesDaEmpresa] ([EmpresaId], [Nivel], [Time], [UsuarioId], [Chave], [Origem]);
GO
