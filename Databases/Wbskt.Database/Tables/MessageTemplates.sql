-- Saved send-panel payloads. Workspace-scoped; optionally pinned to one policy so the
-- console can offer only templates relevant to the selected client's policy.
CREATE TABLE dbo.MessageTemplates (
    Id          INT              IDENTITY(1, 1) NOT NULL,
    RefId       UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
    WorkspaceId INT              NOT NULL,
    PolicyId    INT              NULL,
    Name        NVARCHAR(100)    NOT NULL,
    MessageType NVARCHAR(100)    NOT NULL, -- the SocketMessage type ("topic"), e.g. "cmd/actuate"
    PayloadJson NVARCHAR(MAX)    NOT NULL,
    CreatedAt   DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),
    UpdatedAt   DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_MessageTemplates                      PRIMARY KEY (Id),
    CONSTRAINT UQ_MessageTemplates_RefId                UNIQUE (RefId),
    CONSTRAINT FK_MessageTemplates_RegistrationPolicies FOREIGN KEY (PolicyId)
        REFERENCES dbo.RegistrationPolicies (Id)
);
GO

-- Indexes
CREATE INDEX IX_MessageTemplates_WorkspaceId
    ON dbo.MessageTemplates (WorkspaceId);
GO
