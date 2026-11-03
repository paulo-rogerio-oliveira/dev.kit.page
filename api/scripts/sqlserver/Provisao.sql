-- A provisão no Azure SQL (projeto controle-remoto, item 4): a liberação por usuário, os recursos que o trabalhador cria no
-- Azure com o pedido que os move, e a auditoria de cada mudança de estado.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base VAZIA —
-- numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito) e espelha a
-- migration Provisao do SQLite. Rode-o ANTES de publicar a API com as rotas /api/provisao/v1 e o trabalhador
-- DevKitPage.Provisao (ver docs/publicacao-azure.md).

IF OBJECT_ID(N'[dbo].[LiberacoesDeProvisao]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LiberacoesDeProvisao] (
        [UsuarioId] int NOT NULL,
        [Liberada] bit NOT NULL,
        [AlteradaPor] nvarchar(100) NOT NULL,
        [AlteradaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_LiberacoesDeProvisao] PRIMARY KEY ([UsuarioId]),
        CONSTRAINT [FK_LiberacoesDeProvisao_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- O usuário com recurso não sai em cascata (NO ACTION): o recurso pago continua existindo no Azure.
IF OBJECT_ID(N'[dbo].[RecursosNaNuvem]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RecursosNaNuvem] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [Tipo] nvarchar(20) NOT NULL,
        [Geracao] int NOT NULL,
        [Operacao] nvarchar(20) NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        [Regiao] nvarchar(40) NOT NULL,
        [GrupoDeRecursos] nvarchar(90) NOT NULL,
        [Conta] nvarchar(24) NOT NULL,
        [Fila] nvarchar(63) NOT NULL,
        [Tentativas] int NOT NULL,
        [ProximaTentativaEmUtc] datetime2 NOT NULL,
        [ConcessaoAteUtc] datetime2 NULL,
        [Trabalhador] nvarchar(100) NOT NULL,
        [UltimoErro] nvarchar(500) NOT NULL,
        [ChaveDoCliente] nvarchar(64) NOT NULL,
        [PedidoEmUtc] datetime2 NOT NULL,
        [AtualizadoEmUtc] datetime2 NOT NULL,
        [ProntoEmUtc] datetime2 NULL,
        CONSTRAINT [PK_RecursosNaNuvem] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RecursosNaNuvem_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF OBJECT_ID(N'[dbo].[EventosDaProvisao]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EventosDaProvisao] (
        [Id] bigint NOT NULL IDENTITY,
        [RecursoId] bigint NOT NULL,
        [EmUtc] datetime2 NOT NULL,
        [Operacao] nvarchar(20) NOT NULL,
        [Estado] nvarchar(20) NOT NULL,
        [Autor] nvarchar(120) NOT NULL,
        [Mensagem] nvarchar(500) NOT NULL,
        CONSTRAINT [PK_EventosDaProvisao] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventosDaProvisao_RecursosNaNuvem_RecursoId] FOREIGN KEY ([RecursoId]) REFERENCES [dbo].[RecursosNaNuvem] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- Uma fila por (usuário, tipo, geração): dois pedidos ao mesmo tempo não criam duas.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecursosNaNuvem_UsuarioId_Tipo_Geracao' AND object_id = OBJECT_ID(N'[dbo].[RecursosNaNuvem]'))
    CREATE UNIQUE INDEX [IX_RecursosNaNuvem_UsuarioId_Tipo_Geracao] ON [dbo].[RecursosNaNuvem] ([UsuarioId], [Tipo], [Geracao]);
GO

-- O nome da conta é único no Azure inteiro, e aqui também.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecursosNaNuvem_Conta' AND object_id = OBJECT_ID(N'[dbo].[RecursosNaNuvem]'))
    CREATE UNIQUE INDEX [IX_RecursosNaNuvem_Conta] ON [dbo].[RecursosNaNuvem] ([Conta]);
GO

-- O trabalhador procura os pedidos vencidos pelo estado e pela hora.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecursosNaNuvem_Estado_ProximaTentativaEmUtc' AND object_id = OBJECT_ID(N'[dbo].[RecursosNaNuvem]'))
    CREATE INDEX [IX_RecursosNaNuvem_Estado_ProximaTentativaEmUtc] ON [dbo].[RecursosNaNuvem] ([Estado], [ProximaTentativaEmUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventosDaProvisao_RecursoId' AND object_id = OBJECT_ID(N'[dbo].[EventosDaProvisao]'))
    CREATE INDEX [IX_EventosDaProvisao_RecursoId] ON [dbo].[EventosDaProvisao] ([RecursoId]);
GO
