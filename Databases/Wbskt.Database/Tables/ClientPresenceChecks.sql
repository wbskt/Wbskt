-- A presence change (a client connecting or disconnecting) waiting out a presence trigger's grace
-- period. One row per (trigger, change): the engine's presence ticker leases it once DueAt passes,
-- re-reads the client's presence, and starts a run only if the client is still in that state.
CREATE TABLE dbo.ClientPresenceChecks
(
    Id          INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    TriggerKey  NVARCHAR(400)    NOT NULL,
    ClientRefId UNIQUEIDENTIFIER NOT NULL,
    State       NVARCHAR(16)     NOT NULL, -- 'offline' or 'online'
    ChangedAt   DATETIME2(3)     NOT NULL, -- when the socket host saw the change
    DueAt       DATETIME2(3)     NOT NULL, -- ChangedAt + the trigger's grace period
    LeasedUntil DATETIME2(3)     NULL,
    CreatedAt   DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    -- A redelivered presence event must not park the same change twice.
    CONSTRAINT UQ_ClientPresenceChecks_TriggerKey_ChangedAt
        UNIQUE (TriggerKey, ChangedAt)
);
GO

CREATE INDEX IX_ClientPresenceChecks_DueAt
    ON dbo.ClientPresenceChecks (DueAt);
GO
