CREATE TABLE dbo.JoinAggregators
(
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JoinAggregators PRIMARY KEY,
    JoinToken        UNIQUEIDENTIFIER NOT NULL,
    RunId            INT NOT NULL,
    ExpectedCount    INT NOT NULL,
    ContributedCount INT NOT NULL CONSTRAINT DF_JoinAggregators_Contributed DEFAULT 0,
    SucceededCount   INT NOT NULL CONSTRAINT DF_JoinAggregators_Succeeded DEFAULT 0,
    FailedCount      INT NOT NULL CONSTRAINT DF_JoinAggregators_Failed DEFAULT 0,
    ContinueClaimed  BIT NOT NULL CONSTRAINT DF_JoinAggregators_Claimed DEFAULT 0,
    CreatedAt        DATETIME2(3) NOT NULL CONSTRAINT DF_JoinAggregators_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_JoinAggregators_JoinToken UNIQUE (JoinToken)
);
GO
