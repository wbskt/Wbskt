CREATE TABLE dbo.IdempotencyKeys
(
    Id          BIGINT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
    KeyValue    NVARCHAR(200)    NOT NULL,
    RunId       INT              NOT NULL,
    BranchRefId UNIQUEIDENTIFIER NOT NULL,
    NodeId      UNIQUEIDENTIFIER NOT NULL,
    Attempt     INT              NOT NULL,
    Status      NVARCHAR(16)     NOT NULL,
    ResultJson  NVARCHAR(MAX)    NULL,
    ErrorJson   NVARCHAR(MAX)    NULL,
    CreatedAt   DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAt DATETIME2(3)     NULL,
    CONSTRAINT UQ_IdempotencyKeys_KeyValue UNIQUE (KeyValue),
    -- The words IdempotencyKeyStatus (Wbskt.Workflow.Abstraction) defines.
    CONSTRAINT CK_IdempotencyKeys_Status CHECK (Status = N'Pending' OR Status = N'Succeeded' OR Status = N'Failed')
);
GO

CREATE INDEX IX_IdempotencyKeys_RunId
    ON dbo.IdempotencyKeys (RunId);
GO

-- IdempotencyKey_DeleteExpired sweeps by CreatedAt every hour. This is the fastest-growing table in
-- the schema - one row per inbound event plus one per non-side-effect-free action attempt - so the
-- sweep must seek rather than scan.
CREATE INDEX IX_IdempotencyKeys_CreatedAt
    ON dbo.IdempotencyKeys (CreatedAt);
GO

-- IdempotencyKey_FailAbandonedClaims looks only at Pending rows, which are few at any moment, so a
-- new leader's sweep seeks them rather than scanning a day of settled ones.
CREATE INDEX IX_IdempotencyKeys_Pending
    ON dbo.IdempotencyKeys (KeyValue)
    WHERE Status = N'Pending';
GO
