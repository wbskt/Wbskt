-- Raw device traffic: the events marked [DeviceTraffic] (messages, pongs and latency readings,
-- command delivery and acks, reported values, shared variables a run wrote). They arrive at the rate
-- devices talk, so they live here rather than in dbo.EventLogs: a short retention
-- (EventLogging:DeviceTrafficRetentionDays) swept from the front of the clustered key, and four
-- indexes instead of the audit log's nine. Same columns as dbo.EventLogs, and ids from the same
-- sequence, so dbo.EventLogEntries can read the two as one log.
CREATE TABLE dbo.DeviceTraffic (
    Id            BIGINT           NOT NULL CONSTRAINT DF_DeviceTraffic_Id DEFAULT (NEXT VALUE FOR dbo.EventLogIds),
    EventId       INT              NOT NULL,
    WorkspaceId   INT              NULL,
    PolicyId      INT              NULL,
    PolicyRefId   UNIQUEIDENTIFIER NULL,
    -- NULL for a shared variable a run wrote: workflow traffic, not a device's.
    ClientId      INT              NULL,
    ClientRefId   UNIQUEIDENTIFIER NULL,
    WorkflowId    INT              NULL,
    WorkflowRefId UNIQUEIDENTIFIER NULL,
    UserId        INT              NULL,
    UserRefId     UNIQUEIDENTIFIER NULL,
    EventData     NVARCHAR(MAX)    NULL,
    CreatedAt     DATETIME2(3)     NOT NULL CONSTRAINT DF_DeviceTraffic_CreatedAt DEFAULT SYSUTCDATETIME(),
    MessageId     UNIQUEIDENTIFIER NULL,
    Source        TINYINT          NULL,
    ClientAddress NVARCHAR(45)     NULL,
    UserAgent     NVARCHAR(256)    NULL,

    -- Clustered on the id, which grows with time: inserts append, and the retention sweep
    -- (dbo.DeviceTraffic_DeleteBefore) takes the oldest rows from the front without an index of its own.
    CONSTRAINT PK_DeviceTraffic        PRIMARY KEY (Id),
    CONSTRAINT FK_DeviceTraffic_Events FOREIGN KEY (EventId) REFERENCES dbo.Events(Id)
    -- No foreign key on ClientId or PolicyId, as on dbo.EventLogs: history outlives a deleted client.
);
GO

-- A workspace's log, newest first from a cursor, and its view counts over a time window.
CREATE INDEX IX_DeviceTraffic_WorkspaceId_Id
    ON dbo.DeviceTraffic (WorkspaceId, Id DESC) INCLUDE (EventId, CreatedAt, ClientId, UserRefId, Source);
GO
-- The client page's comms backfill.
CREATE INDEX IX_DeviceTraffic_ClientId_Id
    ON dbo.DeviceTraffic (ClientId, Id DESC) INCLUDE (EventId, WorkspaceId) WHERE ClientId IS NOT NULL;
GO
-- Backs the duplicate check in dbo.DeviceTraffic_InsertBatch, as UX_EventLogs_MessageId does.
CREATE UNIQUE INDEX UX_DeviceTraffic_MessageId
    ON dbo.DeviceTraffic (MessageId, WorkspaceId) WHERE MessageId IS NOT NULL;
GO
