-- O último login do usuário no Azure SQL. Idempotente, espelha a migration 20261031230000_UltimoLoginDoUsuario do SQLite.
-- Rode-o ANTES de publicar a versão da API que grava a coluna (ver docs/publicacao-azure.md): sem ela, todo login falha.
-- Quem já existe fica com a coluna nula: a tela mostra "nunca" até o próximo login.
IF COL_LENGTH(N'dbo.Usuarios', N'UltimoLoginEmUtc') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [UltimoLoginEmUtc] datetime2 NULL;
GO
