CREATE TABLE dbo.EventLogs (
    -- BIGINT: the table takes a row per device message, which would exhaust INT within a year at
    -- modest fleet sizes. Retention (dbo.EventLogs_DeleteBefore) does not reset the identity.
    Id          BIGINT        IDENTITY(1, 1) NOT NULL,
    EventId     INT           NOT NULL,
    WorkspaceId INT           NULL,
    PolicyId    INT           NULL,
    PolicyRefId UNIQUEIDENTIFIER NULL,
    ClientId    INT           NULL,
    ClientRefId UNIQUEIDENTIFIER NULL,
    WorkflowId  INT           NULL,
    WorkflowRefId UNIQUEIDENTIFIER NULL,
    -- Who the event is about, for events that carry IUserContext (logins, token rotation, permission
    -- changes). Auth events have no workspace, so without these they were unattributable.
    UserId      INT           NULL,
    UserRefId   UNIQUEIDENTIFIER NULL,
    EventData   NVARCHAR(MAX) NULL, -- JSON payload
    CreatedAt   DATETIME2(3)  NOT NULL           DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_EventLogs        PRIMARY KEY (Id),
    CONSTRAINT FK_EventLogs_Events FOREIGN KEY (EventId) REFERENCES dbo.Events(Id),
    CONSTRAINT FK_EventLogs_Policy FOREIGN KEY (PolicyId) REFERENCES dbo.RegistrationPolicies(Id)
    -- No foreign key on ClientId: clients can be deleted (dbo.Client_Delete) and their history
    -- outlives them, identified by ClientRefId. A key would also fail a whole buffered insert batch
    -- over one message logged just after its client was deleted.
);
GO

-- Indexes
CREATE INDEX IX_EventLogs_EventId
    ON dbo.EventLogs (EventId);
GO
-- Drives the retention sweep (dbo.EventLogs_DeleteBefore).
CREATE INDEX IX_EventLogs_CreatedAt
    ON dbo.EventLogs (CreatedAt);
GO
-- Both reads are "this workspace (or client), newest first": the key order serves the ORDER BY so a
-- page does not sort the whole history, and EventId covers the join to dbo.Events for the filters.
CREATE INDEX IX_EventLogs_WorkspaceId_CreatedAt
    ON dbo.EventLogs (WorkspaceId, CreatedAt DESC) INCLUDE (EventId);
GO
CREATE INDEX IX_EventLogs_ClientId_CreatedAt
    ON dbo.EventLogs (ClientId, CreatedAt DESC) INCLUDE (EventId, WorkspaceId) WHERE ClientId IS NOT NULL;
GO
CREATE INDEX IX_EventLogs_UserId_CreatedAt
    ON dbo.EventLogs (UserId, CreatedAt DESC) INCLUDE (EventId) WHERE UserId IS NOT NULL;
GO
CREATE INDEX IX_EventLogs_PolicyId
    ON dbo.EventLogs (PolicyId) WHERE PolicyId IS NOT NULL;
GO
CREATE INDEX IX_EventLogs_WorkflowId
    ON dbo.EventLogs (WorkflowId) WHERE WorkflowId IS NOT NULL;
GO
