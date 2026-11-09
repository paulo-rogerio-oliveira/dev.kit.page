-- Os artefatos dos projetos (controle-v4, item 2) no Azure SQL: o zip do pacote .NET guardado no Blob (a chave e o hash) e a
-- URL do preview da página estática, com a retenção do zip.
--
-- Por que um script: com Banco:Provider=SqlServer a API sobe com EnsureCreated, que cria o esquema só numa base VAZIA —
-- numa base que já existe ele não cria tabela nova. Este script é idempotente (pode rodar de novo sem efeito) e espelha a
-- migration ArtefatosDosProjetos do SQLite. Rode-o ANTES de publicar a versão da API com POST /api/projetos/{id}/artefatos;
-- sem ele, a verificação do esquema na subida aponta a tabela e este script.
--
-- Pré-checagem (regra #28): esta versão não traz configuração OBRIGATÓRIA nova. A seção Artefatos é opcional: sem ela, o
-- armazém e a conexão do Blob são os do design (Design__Armazem e Design__ConexaoDoBlob, já conferidos), num contêiner
-- privado 'artefatos'. Falta conferir só a tabela ProjetosCompartilhados, que vem de Us693.sql.

IF OBJECT_ID(N'[dbo].[ProjetosCompartilhados]', N'U') IS NULL
    THROW 50001, N'Rode antes Us693.sql: falta a tabela ProjetosCompartilhados.', 1;
GO

IF OBJECT_ID(N'[dbo].[ArtefatosDosProjetos]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ArtefatosDosProjetos] (
        [Id] bigint NOT NULL IDENTITY,
        [ProjetoId] bigint NOT NULL,
        [Tipo] nvarchar(20) NOT NULL,
        [Repositorio] nvarchar(300) NOT NULL,
        [Sha] nvarchar(40) NOT NULL,
        [Chave] nvarchar(200) NOT NULL,
        [NomeDoArquivo] nvarchar(200) NOT NULL,
        [Url] nvarchar(500) NOT NULL,
        [Ambiente] nvarchar(40) NOT NULL,
        [Hash] nvarchar(64) NOT NULL,
        [Tamanho] bigint NOT NULL,
        [AutorId] int NOT NULL,
        [CriadoEmUtc] datetime2 NOT NULL,
        [RetidoAteUtc] datetime2 NULL,
        CONSTRAINT [PK_ArtefatosDosProjetos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ArtefatosDosProjetos_ProjetosCompartilhados_ProjetoId] FOREIGN KEY ([ProjetoId]) REFERENCES [dbo].[ProjetosCompartilhados] ([Id]) ON DELETE CASCADE
    );
END;
GO

-- Os últimos de cada projeto (o card do Board) e a retenção por projeto.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ArtefatosDosProjetos_ProjetoId_CriadoEmUtc' AND object_id = OBJECT_ID(N'[dbo].[ArtefatosDosProjetos]'))
    CREATE INDEX [IX_ArtefatosDosProjetos_ProjetoId_CriadoEmUtc] ON [dbo].[ArtefatosDosProjetos] ([ProjetoId], [CriadoEmUtc]);
GO
