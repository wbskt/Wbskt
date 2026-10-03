CREATE TABLE dbo.Branches
(
    Id                    INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    RefId                 UNIQUEIDENTIFIER NOT NULL,
    RunId                 INT              NOT NULL,
    ParentBranchId        UNIQUEIDENTIFIER NULL,
    ForkCohortId          UNIQUEIDENTIFIER NULL,
    NodeId                UNIQUEIDENTIFIER NOT NULL,
    Status                NVARCHAR(32)     NOT NULL,
    PendingTakePort       NVARCHAR(128)    NULL,
    LocalJson             NVARCHAR(MAX)    NOT NULL DEFAULT N'{}',
    LastOutputJson        NVARCHAR(MAX)    NULL,
    CompensationStackJson NVARCHAR(MAX)    NULL,
    CreatedAt             DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt             DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    -- Not an optimistic-concurrency guard: branch writes are last-writer-wins by design, since one
    -- engine owns a run's branches. It is the branch's visit token (BranchContext.VisitToken), which
    -- changes on every write and so tells two visits of the same node apart in the idempotency keys
    -- RetryExecutor gives side-effecting actions. Keep it even though no procedure compares it.
    RowVersion            ROWVERSION       NOT NULL,
    CONSTRAINT UQ_Branches_RefId UNIQUE (RefId),
    CONSTRAINT FK_Branches_Runs FOREIGN KEY (RunId) REFERENCES dbo.Runs (Id)
);
GO

CREATE INDEX IX_Branches_RunId
    ON dbo.Branches (RunId);
GO

CREATE INDEX IX_Branches_RunId_Status
    ON dbo.Branches (RunId, Status);
GO

CREATE INDEX IX_Branches_Status_Active
    ON dbo.Branches (Status)
    WHERE Status = N'Active';
GO
