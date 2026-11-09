CREATE TABLE dbo.RunCounters
(
    RunId             INT           NOT NULL PRIMARY KEY,
    ActiveBranchCount INT           NOT NULL DEFAULT 0,
    CreditsConsumed   DECIMAL(18,4) NOT NULL DEFAULT 0,
    -- How much of CreditsConsumed is already in dbo.WorkspaceCreditUsage (dbo.RunCredits_Post).
    PostedCredits     DECIMAL(18,4) NOT NULL CONSTRAINT DF_RunCounters_PostedCredits DEFAULT 0,
    -- A sub-workflow child spends from its root run's budget: BudgetRunId is that run (the parent's own
    -- BudgetRunId, or the parent), NULL for a run with its own budget. The child's charges still add to
    -- its own CreditsConsumed, which is what is posted, and also to the root's ChildCreditsConsumed, which
    -- RunCounters_TryCharge holds the root's budget to beside the root's own CreditsConsumed.
    BudgetRunId          INT           NULL,
    ChildCreditsConsumed DECIMAL(18,4) NOT NULL CONSTRAINT DF_RunCounters_ChildCreditsConsumed DEFAULT 0,
    -- A run in a workspace with a credit cap gets its budget in leases (dbo.Runs.CreditBudget), so one
    -- run going does not reserve the whole cap: RunCounters_TryCharge extends it from what the cap has
    -- left as it runs out, up to BudgetCeiling. NULL for a run whose budget was given whole.
    BudgetCeiling        DECIMAL(18,4) NULL,
    UpdatedAt         DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_RunCounters_Runs FOREIGN KEY (RunId) REFERENCES dbo.Runs (Id)
);
GO
