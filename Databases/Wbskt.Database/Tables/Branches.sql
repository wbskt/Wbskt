CREATE TABLE dbo.Branches
(
    Id                    BIGINT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
    RefId                 UNIQUEIDENTIFIER NOT NULL,
    RunId                 INT              NOT NULL,
    ParentBranchId        UNIQUEIDENTIFIER NULL,
    ForkCohortId          UNIQUEIDENTIFIER NULL,
    NodeId                UNIQUEIDENTIFIER NOT NULL,
    Status                NVARCHAR(32)     NOT NULL,
    PendingTakePort       NVARCHAR(128)    NULL,
    LocalJson             NVARCHAR(MAX)    NOT NULL DEFAULT N'{}',
    LastOutputJson        NVARCHAR(MAX)    NULL,
    CreatedAt             DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt             DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    -- Not an optimistic-concurrency guard: branch writes are last-writer-wins by design, since one
    -- engine owns a run's branches. It is the branch's visit token (BranchContext.VisitToken), which
    -- changes on every write and so tells two visits of the same node apart in the idempotency keys
    -- RetryExecutor gives side-effecting actions. Keep it even though no procedure compares it.
    RowVersion            ROWVERSION       NOT NULL,
    -- When a claim deleted this branch's bookmark to wake it: set in the same transaction as the claim
    -- (with the wake payload or the TTL port it carries), cleared by the next write that moves the
    -- branch. A Waiting branch with WokenAt set has a wake that was decided and stored but may not
    -- have been dispatched; recovery resumes exactly those (Branch_GetWoken).
    WokenAt               DATETIME2(3)     NULL,
    CONSTRAINT UQ_Branches_RefId UNIQUE (RefId),
    -- The words BranchStatus (Wbskt.Workflow.Abstraction) defines; a change there is a change here.
    CONSTRAINT CK_Branches_Status CHECK (Status = N'Active' OR Status = N'Waiting'
        OR Status = N'Completed' OR Status = N'Failed' OR Status = N'Cancelled'),
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

-- For Branch_GetWaitingWithoutBookmark, the leader's periodic sweep for stranded parked branches.
CREATE INDEX IX_Branches_Status_Waiting
    ON dbo.Branches (UpdatedAt)
    INCLUDE (RunId, RefId)
    WHERE Status = N'Waiting';
GO

-- For Branch_GetWoken, the new leader's sweep for branches whose wake was claimed but not dispatched.
CREATE INDEX IX_Branches_Woken
    ON dbo.Branches (Id)
    INCLUDE (RunId, Status)
    WHERE WokenAt IS NOT NULL;
GO
