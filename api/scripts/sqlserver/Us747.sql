-- Executar depois de Us693.sql. Acrescenta o snapshot sem apagar os projetos e itens existentes.
IF COL_LENGTH(N'dbo.ProjetosCompartilhados', N'Documento') IS NULL
    ALTER TABLE [dbo].[ProjetosCompartilhados]
    ADD [Documento] nvarchar(max) NOT NULL CONSTRAINT [DF_ProjetosCompartilhados_Documento] DEFAULT N'';
GO
