CREATE TABLE dbo.JoinAggregators
(
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JoinAggregators PRIMARY KEY,
    JoinToken        UNIQUEIDENTIFIER NOT NULL,
    RunId            INT NOT NULL,
    ExpectedCount    INT NOT NULL,
    -- Mode/QuorumCount are stamped at Initialize rather than passed on each Contribute: a branch that
    -- FAILS contributes from BranchLoop, which has no view of the Join node's config. Storing them
    -- here is what lets a failed arrival be counted at all.
    Mode             NVARCHAR(20) NOT NULL CONSTRAINT DF_JoinAggregators_Mode DEFAULT N'All',
    QuorumCount      INT NOT NULL CONSTRAINT DF_JoinAggregators_QuorumCount DEFAULT 0,
    -- The Join node this cohort converges on, resolved by ParallelForEach at fan-out. BranchLoop
    -- reads it back when a failed contribution satisfies the quorum, so it can spawn the
    -- continuation branch without re-walking the graph.
    JoinNodeId       UNIQUEIDENTIFIER NULL,
    ContributedCount INT NOT NULL CONSTRAINT DF_JoinAggregators_Contributed DEFAULT 0,
    SucceededCount   INT NOT NULL CONSTRAINT DF_JoinAggregators_Succeeded DEFAULT 0,
    FailedCount      INT NOT NULL CONSTRAINT DF_JoinAggregators_Failed DEFAULT 0,
    ContinueClaimed  BIT NOT NULL CONSTRAINT DF_JoinAggregators_Claimed DEFAULT 0,
    CreatedAt        DATETIME2(3) NOT NULL CONSTRAINT DF_JoinAggregators_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_JoinAggregators_JoinToken UNIQUE (JoinToken)
);
GO

-- RunFinalizer deletes a run's aggregators when the run reaches a terminal status.
CREATE INDEX IX_JoinAggregators_RunId
    ON dbo.JoinAggregators (RunId);
GO
