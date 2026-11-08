-- As correções do board remoto (07/11/2026) no Azure SQL: os cards dispensados no site, o contexto do fluxo do comando (o
-- fluxo, o agente e os parâmetros escolhidos no chat), as imagens das falas do Orquestrador e (controle-v4, item 3, 08/11) o
-- dispositivo da decisão.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base VAZIA —
-- numa base que já existe ele não cria tabela nem coluna nova. Este script é idempotente (pode rodar de novo sem efeito) e
-- espelha as migrations BoardRemoto e DispositivoDaDecisao do SQLite. Rode-o ANTES de publicar a versão da API com a ação
-- "dispensar" das decisões; sem ele, a verificação do esquema na subida aponta a tabela, as colunas e este script.
--
-- Pré-checagem (regra #28): esta versão não traz configuração nova (appsettings) — nada a conferir além das tabelas
-- Usuarios, ComandosRemotos e FalasRemotas, que já existem desde os scripts Usuarios.sql e Conversa.sql, e da
-- RespostasDasDecisoes, do Decisoes.sql.

IF OBJECT_ID(N'[dbo].[Usuarios]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[ComandosRemotos]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[FalasRemotas]', N'U') IS NULL
    THROW 50001, N'Rode antes Usuarios.sql e Conversa.sql: faltam as tabelas Usuarios, ComandosRemotos ou FalasRemotas.', 1;
GO

IF OBJECT_ID(N'[dbo].[RespostasDasDecisoes]', N'U') IS NULL
    THROW 50001, N'Rode antes Decisoes.sql: falta a tabela RespostasDasDecisoes.', 1;
GO

IF OBJECT_ID(N'[dbo].[PendenciasDispensadas]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PendenciasDispensadas] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [MaquinaId] nvarchar(64) NOT NULL,
        [PendenciaId] nvarchar(64) NOT NULL,
        [DispensadaEmUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PendenciasDispensadas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PendenciasDispensadas_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[Usuarios] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- Uma dispensa por (usuário, máquina, pendência): o duplo clique não grava duas.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PendenciasDispensadas_UsuarioId_MaquinaId_PendenciaId' AND object_id = OBJECT_ID(N'[dbo].[PendenciasDispensadas]'))
    CREATE UNIQUE INDEX [IX_PendenciasDispensadas_UsuarioId_MaquinaId_PendenciaId] ON [dbo].[PendenciasDispensadas] ([UsuarioId], [MaquinaId], [PendenciaId]);
GO

-- O fluxo, o agente e os parâmetros do comando, em JSON (o reenvio do que falhou na fila leva o mesmo contexto).
IF COL_LENGTH(N'[dbo].[ComandosRemotos]', N'Contexto') IS NULL
    ALTER TABLE [dbo].[ComandosRemotos] ADD [Contexto] nvarchar(max) NOT NULL CONSTRAINT [DF_ComandosRemotos_Contexto] DEFAULT N'';
GO

-- As imagens da fala (o link da API de design, ou o aviso de que a imagem não chegou), em JSON.
IF COL_LENGTH(N'[dbo].[FalasRemotas]', N'Anexos') IS NULL
    ALTER TABLE [dbo].[FalasRemotas] ADD [Anexos] nvarchar(max) NOT NULL CONSTRAINT [DF_FalasRemotas_Anexos] DEFAULT N'';
GO

-- (controle-v4, item 3) O dispositivo da decisão (celular ou navegador), opcional: a decisão gravada antes fica nula.
IF COL_LENGTH(N'[dbo].[RespostasDasDecisoes]', N'Dispositivo') IS NULL
    ALTER TABLE [dbo].[RespostasDasDecisoes] ADD [Dispositivo] nvarchar(20) NULL;
GO
