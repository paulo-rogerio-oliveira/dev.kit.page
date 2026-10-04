-- Tabela dos pedidos de demonstração da landing (US #283) no Azure SQL.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema
-- só numa base VAZIA — numa base que já existe ele não cria tabela nova. Este script é idempotente
-- (pode rodar de novo sem efeito) e espelha a migration 20261004014053_PedidosDeDemonstracao do SQLite.
-- Rode-o ANTES de publicar a versão da API que tem o formulário (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[PedidosDeDemonstracao]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PedidosDeDemonstracao] (
        [Id] bigint NOT NULL IDENTITY,
        [Nome] nvarchar(100) NOT NULL,
        [Email] nvarchar(254) NOT NULL,
        [Empresa] nvarchar(100) NOT NULL,
        [Mensagem] nvarchar(1000) NOT NULL,
        [ConsentimentoEmUtc] datetime2 NOT NULL,
        [RecebidoEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PedidosDeDemonstracao] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_PedidosDeDemonstracao_RecebidoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[PedidosDeDemonstracao]'))
BEGIN
    CREATE INDEX [IX_PedidosDeDemonstracao_RecebidoEmUtc] ON [dbo].[PedidosDeDemonstracao] ([RecebidoEmUtc]);
END;
GO
