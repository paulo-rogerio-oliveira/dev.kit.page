-- O client credentials da máquina (remote-control-v3, item 9) no Azure SQL: o cliente de cada máquina do dev.kit (a chave
-- pública ECDSA do par que o dev.kit gerou, presa ao usuário que a vinculou) e os jti já gastos das asserções.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base VAZIA —
-- numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito) e espelha a
-- migration ClienteDaMaquina do SQLite. Rode-o ANTES de publicar a versão da API com POST /api/auth/token (ver
-- docs/publicacao-azure.md); sem ele, a verificação do esquema na subida aponta as tabelas e este script.

IF OBJECT_ID(N'[dbo].[ClientesDaMaquina]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ClientesDaMaquina] (
        [Id] int NOT NULL IDENTITY,
        [MaquinaId] int NOT NULL,
        [UsuarioId] int NOT NULL,
        [ChavePublica] nvarchar(200) NOT NULL,
        [Carimbo] nvarchar(16) NOT NULL,
        [RegistradoEmUtc] datetime2 NOT NULL,
        [RevogadoEmUtc] datetime2 NULL,
        [UltimoTokenEmUtc] datetime2 NULL,
        CONSTRAINT [PK_ClientesDaMaquina] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ClientesDaMaquina_Maquinas_MaquinaId] FOREIGN KEY ([MaquinaId]) REFERENCES [dbo].[Maquinas] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ClientesDaMaquina_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- Um cliente por máquina: o vínculo novo substitui o anterior.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ClientesDaMaquina_MaquinaId' AND object_id = OBJECT_ID(N'[dbo].[ClientesDaMaquina]'))
    CREATE UNIQUE INDEX [IX_ClientesDaMaquina_MaquinaId] ON [dbo].[ClientesDaMaquina] ([MaquinaId]);
GO

-- As máquinas vinculadas do usuário (a lista do site).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ClientesDaMaquina_UsuarioId' AND object_id = OBJECT_ID(N'[dbo].[ClientesDaMaquina]'))
    CREATE INDEX [IX_ClientesDaMaquina_UsuarioId] ON [dbo].[ClientesDaMaquina] ([UsuarioId]);
GO

IF OBJECT_ID(N'[dbo].[AssercoesDoCliente]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AssercoesDoCliente] (
        [Id] bigint NOT NULL IDENTITY,
        [ClienteId] int NOT NULL,
        [Jti] nvarchar(128) NOT NULL,
        [ExpiraEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AssercoesDoCliente] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AssercoesDoCliente_ClientesDaMaquina_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [dbo].[ClientesDaMaquina] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- O uso único da asserção: o mesmo jti do mesmo cliente não entra duas vezes.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssercoesDoCliente_ClienteId_Jti' AND object_id = OBJECT_ID(N'[dbo].[AssercoesDoCliente]'))
    CREATE UNIQUE INDEX [IX_AssercoesDoCliente_ClienteId_Jti] ON [dbo].[AssercoesDoCliente] ([ClienteId], [Jti]);
GO

-- O expurgo diário leva os vencidos.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssercoesDoCliente_ExpiraEmUtc' AND object_id = OBJECT_ID(N'[dbo].[AssercoesDoCliente]'))
    CREATE INDEX [IX_AssercoesDoCliente_ExpiraEmUtc] ON [dbo].[AssercoesDoCliente] ([ExpiraEmUtc]);
GO
