CREATE TABLE dbo.EventLogs (
    Id          INT           IDENTITY(1, 1) NOT NULL,
    EventId     INT           NOT NULL,
    WorkspaceId INT           NULL,
    PolicyId    INT           NULL,
    PolicyRefId UNIQUEIDENTIFIER NULL,
    ClientId    INT           NULL,
    ClientRefId UNIQUEIDENTIFIER NULL,
    WorkflowId  INT           NULL,
    WorkflowRefId UNIQUEIDENTIFIER NULL,
    EventData   NVARCHAR(MAX) NULL, -- JSON payload
    CreatedAt   DATETIME2(3)  NOT NULL           DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_EventLogs        PRIMARY KEY (Id),
    CONSTRAINT FK_EventLogs_Events FOREIGN KEY (EventId) REFERENCES dbo.Events(Id),
    CONSTRAINT FK_EventLogs_Policy FOREIGN KEY (PolicyId) REFERENCES dbo.RegistrationPolicies(Id),
    CONSTRAINT FK_EventLogs_Client FOREIGN KEY (ClientId) REFERENCES dbo.Clients(Id),
    CONSTRAINT FK_EventLogs_Workflow FOREIGN KEY (WorkflowId) REFERENCES dbo.Workflows(Id)
);
GO

-- Indexes
CREATE INDEX IX_EventLogs_EventId
    ON dbo.EventLogs (EventId);
GO
CREATE INDEX IX_EventLogs_CreatedAt
    ON dbo.EventLogs (CreatedAt);
GO
CREATE INDEX IX_EventLogs_WorkspaceId
    ON dbo.EventLogs (WorkspaceId); -- Recommended for multi-tenant filtering
GO
CREATE INDEX IX_EventLogs_PolicyId
    ON dbo.EventLogs (PolicyId) WHERE PolicyId IS NOT NULL;
GO
CREATE INDEX IX_EventLogs_ClientId
    ON dbo.EventLogs (ClientId) WHERE ClientId IS NOT NULL;
GO
CREATE INDEX IX_EventLogs_WorkflowRefId
    ON dbo.EventLogs (WorkflowId) WHERE WorkflowId IS NOT NULL;
GO