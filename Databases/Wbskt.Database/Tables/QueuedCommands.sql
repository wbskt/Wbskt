-- Commands sent to a device while it was offline, held until it connects or they expire. A row lives
-- only while its command waits: delivery, expiry, cancellation and the device being revoked or deleted
-- each take it out, and what became of the command is in the event log.
-- The actor columns carry who sent it, so the command published at delivery is attributed to them
-- rather than to the background work that delivers it.
CREATE TABLE dbo.QueuedCommands (
    Id                 BIGINT           NOT NULL IDENTITY(1,1),
    CommandId          UNIQUEIDENTIFIER NOT NULL,
    ClientId           INT              NOT NULL,
    ClientRefId        UNIQUEIDENTIFIER NOT NULL,
    WorkspaceId        INT              NOT NULL,
    Type               NVARCHAR(100)    NOT NULL,
    Payload            NVARCHAR(MAX)    NOT NULL,
    ExpiresAt          DATETIME2(3)     NOT NULL,
    QueuedAt           DATETIME2(3)     NOT NULL CONSTRAINT DF_QueuedCommands_QueuedAt DEFAULT SYSUTCDATETIME(),
    ActorUserId        INT              NULL,
    ActorUserRefId     UNIQUEIDENTIFIER NULL,
    ActorSource        TINYINT          NULL,
    ActorWorkflowRefId UNIQUEIDENTIFIER NULL,
    ActorRunRefId      UNIQUEIDENTIFIER NULL,

    -- Constraints
    CONSTRAINT PK_QueuedCommands           PRIMARY KEY (Id),
    CONSTRAINT UQ_QueuedCommands_CommandId UNIQUE (CommandId),
    CONSTRAINT FK_QueuedCommands_Clients   FOREIGN KEY (ClientId)
        REFERENCES dbo.Clients (Id)
);
GO

-- A device's queue, oldest first: delivery on connect, the per-device limit and the waiting list.
CREATE INDEX IX_QueuedCommands_ClientId
    ON dbo.QueuedCommands (ClientId, Id);
GO

-- The expiry sweep reads from the front.
CREATE INDEX IX_QueuedCommands_ExpiresAt
    ON dbo.QueuedCommands (ExpiresAt);
GO
