-- Sets @WorkspaceId's monthly credit cap, or removes it with @MonthlyCreditCap NULL. Returns the cap and
-- what the workspace has spent since @MonthStart, so a caller lowering the cap below that can say new
-- runs are refused at once.
--
-- Setting a cap also brings the workspace's root runs still going under it, under the cap's lock as
-- Run_Start and RunCounters_TryCharge take it: each one's budget is cut to what it has spent, and one
-- that was given its whole budget (no cap when it started) keeps that budget as its ceiling. Its next
-- charge then goes through RunCounters_TryCharge's lease path, which extends it only from what the cap
-- has left, so a run already going cannot spend past a cap set while it runs, and the unspent budgets it
-- held stop counting as reserved against the cap. The counters are not locked: a charge landing as the
-- budget is cut was already allowed by the old budget, and the next one goes through the lease.
-- 50035 when the lock is not free within five seconds.
CREATE PROCEDURE dbo.WorkspaceCreditCap_Set
    @WorkspaceId      INT,
    @MonthlyCreditCap DECIMAL(18,4),
    @MonthStart       DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF @MonthlyCreditCap IS NULL
    BEGIN
        DELETE FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = @WorkspaceId;
    END
    ELSE
    BEGIN
        DECLARE @CapResource NVARCHAR(255) = CONCAT(N'credit-cap:', @WorkspaceId);
        DECLARE @CapLockResult INT;
        EXEC @CapLockResult = sp_getapplock @Resource = @CapResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
        IF @CapLockResult < 0
            THROW 50035, 'The workspace is too busy to set its credit cap; try again.', 1;

        MERGE dbo.WorkspaceCreditCaps WITH (HOLDLOCK) AS t
        USING (SELECT @WorkspaceId AS WorkspaceId) AS s
        ON t.WorkspaceId = s.WorkspaceId
        WHEN MATCHED THEN
            UPDATE SET MonthlyCreditCap = @MonthlyCreditCap, UpdatedAt = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT (WorkspaceId, MonthlyCreditCap) VALUES (@WorkspaceId, @MonthlyCreditCap);

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
