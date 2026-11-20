CREATE TABLE dbo.RegistrationPolicies (
    Id           INT              IDENTITY(1, 1) NOT NULL,
    RefId        UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
    WorkspaceId  INT              NOT NULL,
    Pin          NVARCHAR(20)     NOT NULL,
    Name         NVARCHAR(100)    NOT NULL,
    MaxClients   INT              NULL,
    AutoApproval BIT              NOT NULL           DEFAULT 1,
    IsEnabled    BIT              NOT NULL           DEFAULT 1,
    CreatedAt    DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),
    -- When the PIN was last drawn: on create and on each rotation. NULL on policies made before the
    -- column, whose PIN is read as dating from CreatedAt.
    PinRotatedAt DATETIME2(3)     NULL,

    -- Constraints
    CONSTRAINT PK_RegistrationPolicies       PRIMARY KEY (Id),
    CONSTRAINT UQ_RegistrationPolicies_RefId UNIQUE (RefId),
    CONSTRAINT UQ_RegistrationPolicies_Pin   UNIQUE (Pin)
);
GO

-- A workspace's policies: its policy list and Workspace_GetCounts.
CREATE INDEX IX_RegistrationPolicies_WorkspaceId
    ON dbo.RegistrationPolicies (WorkspaceId);
GO
