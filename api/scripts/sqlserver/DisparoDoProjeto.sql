-- A marca de disparo dos projetos (boards-estados, item 5) no Azure SQL: a coluna opcional DisparadoEmUtc dos
-- ProjetosCompartilhados, que a lista de resumos (GET /api/projetos/resumos) lê para separar o aprovado que aguarda o
-- disparo do em execução, sem ler o documento do projeto. O nível e os estados do espelho no Azure Boards vão no
-- documento (Documento) e não precisam de coluna.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base VAZIA —
-- numa base que já existe ele não cria coluna nova. Este script é idempotente (pode rodar de novo sem efeito), espelha a
-- migration DisparoDoProjeto do SQLite e é compatível com a API anterior: a coluna é nula, nenhum projeto muda de
-- situação e nada é apagado. A marca não é inventada aqui: ela chega do host (dev.kit) na sincronização seguinte.
-- Aplique-o pelo job sql do main_devkit.yml (aplicar_sql) ANTES da API que lê a marca; sem ele, a verificação do esquema
-- na subida aponta a coluna e este script.

IF OBJECT_ID(N'[dbo].[ProjetosCompartilhados]', N'U') IS NULL
    THROW 50001, N'Rode antes Us693.sql: falta a tabela ProjetosCompartilhados.', 1;
GO

IF COL_LENGTH(N'dbo.ProjetosCompartilhados', N'DisparadoEmUtc') IS NULL
    ALTER TABLE [dbo].[ProjetosCompartilhados] ADD [DisparadoEmUtc] datetime2 NULL;
GO
