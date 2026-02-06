CREATE TABLE dbo.Clients (
    Id INT IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    RefId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() UNIQUE,
    PolicyId INT NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    Secret NVARCHAR(255) NOT NULL,
    Status TINYINT NOT NULL DEFAULT 0, -- 0: Pending, 1: Registered, 2: Revoked
    CreatedAt DATETIME2(0) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Clients_RegistrationPolicies FOREIGN KEY (PolicyId) REFERENCES dbo.RegistrationPolicies(Id)
);
GO

CREATE INDEX IX_Clients_PolicyId ON dbo.Clients(PolicyId);
GO

CREATE INDEX IX_Clients_RefId ON dbo.Clients(RefId);
GO
