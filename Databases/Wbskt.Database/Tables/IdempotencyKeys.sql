CREATE TABLE dbo.IdempotencyKeys
(
    Id          INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
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
    CONSTRAINT UQ_IdempotencyKeys_KeyValue UNIQUE (KeyValue)
);
GO

CREATE INDEX IX_IdempotencyKeys_RunId
    ON dbo.IdempotencyKeys (RunId);
GO
