-- Where each client trigger with a hold time stands: one row per trigger key. 'holding' means its
-- filter has matched every message since SinceAt; the engine's hold ticker starts a run once DueAt
-- passes and marks the row 'fired', and the trigger stays quiet until a message stops matching
-- ('clear'). LastEventAt orders messages: one older than it arrived late and is ignored.
CREATE TABLE dbo.ClientHoldStates
(
    Id          INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    TriggerKey  NVARCHAR(400)    NOT NULL,
    State       NVARCHAR(16)     NOT NULL, -- 'holding', 'fired' or 'clear'
    LastEventAt DATETIME2(3)     NOT NULL,
    SinceAt     DATETIME2(3)     NULL,     -- first matching message of the current hold
    DueAt       DATETIME2(3)     NULL,     -- SinceAt + the trigger's hold time
    Payload     NVARCHAR(MAX)    NULL,     -- latest matching message, as the run's trigger payload
    LeasedUntil DATETIME2(3)     NULL,
    UpdatedAt   DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_ClientHoldStates_TriggerKey UNIQUE (TriggerKey)
);
GO

CREATE INDEX IX_ClientHoldStates_State_DueAt
    ON dbo.ClientHoldStates (State, DueAt);
GO
