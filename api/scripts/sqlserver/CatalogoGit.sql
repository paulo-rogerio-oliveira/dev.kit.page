-- Aplicar junto da API, pelo caminho de provisionamento autorizado. Sem dados nem credenciais.
IF OBJECT_ID(N'dbo.RecibosDoCatalogoGit', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RecibosDoCatalogoGit] (
        Sha nvarchar(40) NOT NULL CONSTRAINT PK_RecibosDoCatalogoGit PRIMARY KEY,
        VersaoId bigint NOT NULL,
        Documento nvarchar(max) NOT NULL,
        CONSTRAINT FK_RecibosDoCatalogoGit_VersoesDoCatalogo_VersaoId FOREIGN KEY (VersaoId)
            REFERENCES dbo.VersoesDoCatalogo(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX IX_RecibosDoCatalogoGit_VersaoId ON dbo.RecibosDoCatalogoGit(VersaoId);
END;
