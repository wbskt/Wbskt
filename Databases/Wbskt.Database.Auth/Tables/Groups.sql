CREATE TABLE dbo.Groups (
    Id            INT              IDENTITY(1, 1) NOT NULL,
    RefId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Groups_RefId DEFAULT NEWID(),
    TenantId      INT              NOT NULL,
    Name          NVARCHAR(100)    NOT NULL,
    ParentGroupId INT              NULL,

    -- Constraints
    CONSTRAINT PK_Groups         PRIMARY KEY (Id),
    CONSTRAINT UQ_Groups_RefId   UNIQUE (RefId),
    CONSTRAINT UQ_Groups_Name    UNIQUE (TenantId, Name),
    CONSTRAINT FK_Groups_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_Groups_Parent  FOREIGN KEY (ParentGroupId) REFERENCES dbo.Groups (Id)
);
GO
