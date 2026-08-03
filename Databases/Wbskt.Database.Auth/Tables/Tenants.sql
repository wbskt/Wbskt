-- Name is deliberately not unique. Every registration without an invitation creates a tenant named
-- after the user, so a global uniqueness constraint would make sign-up fail on a name the user
-- never chose and cannot see. A tenant is identified by RefId; Name is a display label.
CREATE TABLE dbo.Tenants (
    Id INT IDENTITY(1, 1) NOT NULL,
    RefId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED (Id ASC),
    CONSTRAINT UQ_Tenants_RefId UNIQUE (RefId)
);
GO
