IF COL_LENGTH('Catalogos', 'Origem') IS NULL
    ALTER TABLE Catalogos ADD Origem nvarchar(max) NULL;
IF COL_LENGTH('Catalogos', 'MinDevKitDaOrigem') IS NULL
    ALTER TABLE Catalogos ADD MinDevKitDaOrigem int NULL;
