-- A US #550 no Azure SQL: o CONTEXTO do dev.kit (as chaves contexto.<tema> das convenções da #491) é texto de várias
-- linhas, com teto de 4000 caracteres — a coluna Valor de ConvencoesDaEmpresa cresce de nvarchar(200) para nvarchar(4000).
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que não altera coluna numa base que
-- já existe. Este script é idempotente (só altera enquanto a coluna for menor) e espelha a migration
-- Us550ContextoPorNivel do SQLite. Rode-o DEPOIS do Us491.sql e ANTES de publicar a versão da API com o contexto (ver
-- docs/publicacao-azure.md).

IF COL_LENGTH(N'[dbo].[ConvencoesDaEmpresa]', N'Valor') IS NOT NULL
   AND COL_LENGTH(N'[dbo].[ConvencoesDaEmpresa]', N'Valor') < 8000 -- em bytes: nvarchar(4000) ocupa 8000
    ALTER TABLE [dbo].[ConvencoesDaEmpresa] ALTER COLUMN [Valor] nvarchar(4000) NOT NULL;
GO
