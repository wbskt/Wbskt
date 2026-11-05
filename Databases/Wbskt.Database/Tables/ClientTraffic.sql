-- Raw client traffic: the events marked [ClientTraffic] (messages, pongs and latency readings,
-- command delivery and acks, reported values, shared variables a run wrote). They arrive at the rate
-- clients talk, so they live here rather than in dbo.EventLogs: a short retention
-- (EventLogging:ClientTrafficRetentionDays) swept from the front of the clustered key, and four
-- indexes instead of the audit log's nine. Same columns as dbo.EventLogs, and ids from the same
-- sequence, so dbo.EventLogEntries can read the two as one log.
CREATE TABLE dbo.ClientTraffic (
    Id            BIGINT           NOT NULL CONSTRAINT DF_ClientTraffic_Id DEFAULT (NEXT VALUE FOR dbo.EventLogIds),
    EventId       INT              NOT NULL,
    WorkspaceId   INT              NULL,
    PolicyId      INT              NULL,
    PolicyRefId   UNIQUEIDENTIFIER NULL,
    -- NULL for a shared variable a run wrote: workflow traffic, not a client's.
    ClientId      INT              NULL,
    ClientRefId   UNIQUEIDENTIFIER NULL,
    WorkflowId    INT              NULL,
    WorkflowRefId UNIQUEIDENTIFIER NULL,
    UserId        INT              NULL,
    UserRefId     UNIQUEIDENTIFIER NULL,
    EventData     NVARCHAR(MAX)    NULL,
    CreatedAt     DATETIME2(3)     NOT NULL CONSTRAINT DF_ClientTraffic_CreatedAt DEFAULT SYSUTCDATETIME(),
    MessageId     UNIQUEIDENTIFIER NULL,
    Source        TINYINT          NULL,
    ClientAddress NVARCHAR(45)     NULL,
    UserAgent     NVARCHAR(256)    NULL,

    -- Clustered on the id, which grows with time: inserts append, and the retention sweep
    -- (dbo.ClientTraffic_DeleteBefore) takes the oldest rows from the front without an index of its own.
    CONSTRAINT PK_ClientTraffic        PRIMARY KEY (Id),
    CONSTRAINT FK_ClientTraffic_Events FOREIGN KEY (EventId) REFERENCES dbo.Events(Id)
    -- No foreign key on ClientId or PolicyId, as on dbo.EventLogs: history outlives a deleted client.
);
GO

-- A workspace's log, newest first from a cursor, and its view counts over a time window.
CREATE INDEX IX_ClientTraffic_WorkspaceId_Id
    ON dbo.ClientTraffic (WorkspaceId, Id DESC) INCLUDE (EventId, CreatedAt, ClientId, UserRefId, Source);
GO
-- The client page's comms backfill.
CREATE INDEX IX_ClientTraffic_ClientId_Id
    ON dbo.ClientTraffic (ClientId, Id DESC) INCLUDE (EventId, WorkspaceId) WHERE ClientId IS NOT NULL;
GO
-- Backs the duplicate check in dbo.ClientTraffic_InsertBatch, as UX_EventLogs_MessageId does.
CREATE UNIQUE INDEX UX_ClientTraffic_MessageId
    ON dbo.ClientTraffic (MessageId, WorkspaceId) WHERE MessageId IS NOT NULL;
GO
