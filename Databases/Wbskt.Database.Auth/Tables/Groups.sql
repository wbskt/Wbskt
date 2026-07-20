CREATE TABLE dbo.Groups (
    Id            INT           IDENTITY(1, 1) NOT NULL,
    TenantId      INT           NOT NULL CONSTRAINT DF_Groups_TenantId DEFAULT (1),
    Name          NVARCHAR(100) NOT NULL,
    ParentGroupId INT           NULL,

    -- Constraints
    CONSTRAINT PK_Groups         PRIMARY KEY (Id),
    CONSTRAINT UQ_Groups_Name    UNIQUE (TenantId, Name),
    CONSTRAINT FK_Groups_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_Groups_Parent  FOREIGN KEY (ParentGroupId) REFERENCES dbo.Groups (Id)
);
GO
