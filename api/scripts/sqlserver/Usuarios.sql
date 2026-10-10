-- A gestão de usuários (US #405) no Azure SQL: o nome de exibição e o bloqueio pelo admin.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema
-- só numa base VAZIA — numa base que já existe ele não cria coluna nova. Este script é idempotente
-- (pode rodar de novo sem efeito) e espelha a migration 20261009191400_GestaoDeUsuarios do SQLite.
-- Rode-o ANTES de publicar a versão da API com a gestão de usuários (ver docs/publicacao-azure.md).
-- O papel "dev" não precisa de nada aqui: a coluna Papel já é texto (Empresas.sql).

-- O nome de exibição: os usuários que já existem ficam com o nome vazio (a tela mostra o login).
IF COL_LENGTH(N'dbo.Usuarios', N'Nome') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [Nome] nvarchar(100) NOT NULL CONSTRAINT [DF_Usuarios_Nome] DEFAULT N'';
GO

-- O bloqueio pelo admin (o temporário, por falhas seguidas, continua em BloqueadoAteUtc).
IF COL_LENGTH(N'dbo.Usuarios', N'BloqueadoPeloAdmin') IS NULL
    ALTER TABLE [dbo].[Usuarios] ADD [BloqueadoPeloAdmin] bit NOT NULL CONSTRAINT [DF_Usuarios_BloqueadoPeloAdmin] DEFAULT CAST(0 AS bit);
GO
