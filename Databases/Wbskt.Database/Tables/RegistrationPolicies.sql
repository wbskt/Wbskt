CREATE TABLE dbo.RegistrationPolicies (
    Id           INT              IDENTITY(1, 1) NOT NULL,
    RefId        UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
    WorkspaceId  INT              NOT NULL,
    Pin          NVARCHAR(10)     NOT NULL,
    Name         NVARCHAR(100)    NOT NULL,
    MaxClients   INT              NULL,
    AutoApproval BIT              NOT NULL           DEFAULT 1,
    CreatedAt    DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_RegistrationPolicies       PRIMARY KEY (Id),
    CONSTRAINT UQ_RegistrationPolicies_RefId UNIQUE (RefId),
    CONSTRAINT UQ_RegistrationPolicies_Pin   UNIQUE (Pin)
);
GO

-- Indexes
CREATE INDEX IX_RegistrationPolicies_RefId
    ON dbo.RegistrationPolicies (RefId);
GO