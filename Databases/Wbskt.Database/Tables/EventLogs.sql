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
    -- The bus message the row was written from. The event log consumer saves a batch before it
    -- acknowledges it, so a crash in between redelivers messages already saved; the insert skips a
    -- MessageId that is already here. Last, so adding it to an existing table needs no rebuild.
    MessageId   UNIQUEIDENTIFIER NULL,
    -- Where it came from (Wbskt.Events.Abstractions.EventSource): 1 console, 2 API, 3 device,
    -- 4 workflow, 5 system. NULL for rows from before the column, and for sign-ins and other changes a
    -- person makes to their own account.
    Source      TINYINT       NULL,
    -- The caller's address and user agent, for an action taken through the API. On the row rather
    -- than in EventData so they go with the row at the audit retention, and nowhere else.
    ClientAddress NVARCHAR(45) NULL,
    UserAgent   NVARCHAR(256) NULL,

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
-- Both reads are "this workspace (or client), newest first", a page at a time from a cursor (the Id
-- of the last row the caller has). Keyed on Id so a page is a seek past the cursor rather than an
-- OFFSET over everything before it; EventId covers the join to dbo.Events for the filters.
CREATE INDEX IX_EventLogs_WorkspaceId_Id
    ON dbo.EventLogs (WorkspaceId, Id DESC) INCLUDE (EventId);
GO
-- A time window of one workspace: the audit log's view counts (dbo.EventLog_CountBy_Workspace).
CREATE INDEX IX_EventLogs_WorkspaceId_CreatedAt
    ON dbo.EventLogs (WorkspaceId, CreatedAt) INCLUDE (EventId, UserRefId, Source);
GO
CREATE INDEX IX_EventLogs_ClientId_Id
    ON dbo.EventLogs (ClientId, Id DESC) INCLUDE (EventId, WorkspaceId) WHERE ClientId IS NOT NULL;
GO
CREATE INDEX IX_EventLogs_UserId_CreatedAt
    ON dbo.EventLogs (UserId, CreatedAt DESC) INCLUDE (EventId) WHERE UserId IS NOT NULL;
GO
CREATE INDEX IX_EventLogs_PolicyId
    ON dbo.EventLogs (PolicyId) WHERE PolicyId IS NOT NULL;
GO
-- Backs the duplicate check in dbo.EventLogs_InsertBatch, and stops two consumers that receive the
-- same redelivered message at once from both inserting it. Rows from before the column are NULL.
-- Per workspace: a sign-in or a change to a tenant's people is one message logged in each of its
-- workspaces.
CREATE UNIQUE INDEX UX_EventLogs_MessageId
    ON dbo.EventLogs (MessageId, WorkspaceId) WHERE MessageId IS NOT NULL;
GO
CREATE INDEX IX_EventLogs_WorkflowId
    ON dbo.EventLogs (WorkflowId) WHERE WorkflowId IS NOT NULL;
GO
