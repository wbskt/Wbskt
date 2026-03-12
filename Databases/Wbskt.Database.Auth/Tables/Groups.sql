CREATE TABLE dbo.Groups (
    Id            INT           IDENTITY(1, 1) NOT NULL,
    Name          NVARCHAR(100) NOT NULL,
    ParentGroupId INT           NULL,

    -- Constraints
    CONSTRAINT PK_Groups        PRIMARY KEY (Id),
    CONSTRAINT UQ_Groups_Name   UNIQUE (Name),
    CONSTRAINT FK_Groups_Parent FOREIGN KEY (ParentGroupId) REFERENCES dbo.Groups (Id)
);
GO
