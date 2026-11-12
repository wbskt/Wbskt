-- Sets @WorkspaceId's monthly credit cap, or removes it with @MonthlyCreditCap NULL. Returns the cap and
-- what the workspace has spent since @MonthStart, so a caller lowering the cap below that can say new
-- runs are refused at once.
--
-- The cap row is written first. Run_Start reads it WITH (HOLDLOCK) and holds that to its commit, so the
-- write waits for a start already past that read, and a start after it waits for this commit and sees
-- the new cap: no run starts under the old cap once this one is in.
--
-- Setting a cap also brings the workspace's root runs still going under it, under the cap's lock as
-- Run_Start and RunCounters_TryCharge take it: each one's budget is cut to what it has spent, and one
-- that was given its whole budget (no cap when it started) keeps that budget as its ceiling. Its next
-- charge then goes through RunCounters_TryCharge's lease path, which extends it only from what the cap
-- has left, so a run already going cannot spend past a cap set while it runs, and the unspent budgets it
-- held stop counting as reserved against the cap. Charges already under way as the budget is cut (one
-- per branch charging at that moment) read the run's budget from before the cut and can still land, never
-- past that old budget; the lease path counts what they add as spent rather than as budget.
--
-- 50035 when the cap's lock is not free within two seconds, and 1222 when a row it needs is not free
-- within five: the caller answers 503 and the owner tries again. For the same reason its deadlock
-- priority is low, so a deadlock with the engine (RunCredits_Post, a run ending) rolls back this set
-- rather than the engine's work.
CREATE PROCEDURE dbo.WorkspaceCreditCap_Set
    @WorkspaceId      INT,
    @MonthlyCreditCap DECIMAL(18,4),
    @MonthStart       DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET DEADLOCK_PRIORITY LOW;
    SET LOCK_TIMEOUT 5000;

    BEGIN TRANSACTION;

    IF @MonthlyCreditCap IS NULL
    BEGIN
        DELETE FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = @WorkspaceId;
    END
    ELSE
    BEGIN
        MERGE dbo.WorkspaceCreditCaps WITH (HOLDLOCK) AS t
        USING (SELECT @WorkspaceId AS WorkspaceId) AS s
        ON t.WorkspaceId = s.WorkspaceId
        WHEN MATCHED THEN
            UPDATE SET MonthlyCreditCap = @MonthlyCreditCap,
                       -- A raised cap is announced again as its new thresholds are reached, as a cap
                       -- removed and set again is (the auth host still emails once a month).
                       NotifiedPercent = CASE WHEN @MonthlyCreditCap > t.MonthlyCreditCap THEN 0 ELSE t.NotifiedPercent END,
                       UpdatedAt = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT (WorkspaceId, MonthlyCreditCap) VALUES (@WorkspaceId, @MonthlyCreditCap);

        DECLARE @CapResource NVARCHAR(255) = CONCAT(N'credit-cap:', @WorkspaceId);
        DECLARE @CapLockResult INT;
        EXEC @CapLockResult = sp_getapplock @Resource = @CapResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 2000;
        IF @CapLockResult < 0
            THROW 50035, 'The workspace is too busy to set its credit cap; try again.', 1;

        -- The ceiling first, from the budget before it is cut. The Status IN list is
        -- IX_Runs_WorkflowDefinitionId_Active's filter word for word.
        UPDATE rc
        SET rc.BudgetCeiling = r.CreditBudget,
            rc.UpdatedAt = SYSUTCDATETIME()
        FROM dbo.RunCounters rc
        JOIN dbo.Runs r ON r.Id = rc.RunId
        JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
        WHERE d.WorkspaceId = @WorkspaceId
          AND r.Status IN (N'Running', N'Failing', N'Cancelling')
          AND rc.BudgetRunId IS NULL
          AND rc.BudgetCeiling IS NULL;

        UPDATE r
        SET r.CreditBudget = rc.CreditsConsumed + rc.ChildCreditsConsumed
        FROM dbo.Runs r
        JOIN dbo.RunCounters rc ON rc.RunId = r.Id
        JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
        WHERE d.WorkspaceId = @WorkspaceId
          AND r.Status IN (N'Running', N'Failing', N'Cancelling')
          AND rc.BudgetRunId IS NULL
          AND r.CreditBudget > rc.CreditsConsumed + rc.ChildCreditsConsumed;
    END

    COMMIT TRANSACTION;

    SELECT
        @MonthlyCreditCap AS MonthlyCreditCap,
        s.Credits AS CreditsSpent
    FROM dbo.WorkspaceCreditsSpent(@WorkspaceId, @MonthStart) s;
END;
GO
