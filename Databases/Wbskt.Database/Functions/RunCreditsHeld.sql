-- The credits runs hold against a workspace's cap or a tenant's allowance, one row per run and kind:
--   Reserved  what a run still going may yet spend: its own budget less what it and the sub-workflow
--             children sharing it consumed (children hold nothing of their own, BudgetRunId set);
--   Unposted  what a run consumed and RunCredits_Post has not moved to the ledger yet, ended or not, so
--             a run that ends between two posts does not hand its spending back.
-- HeldFromAllowance is 1 for a root run that leased its budget from the plan's monthly credits
-- (dbo.RunCounters.CreditAllowance, set by Run_Start); a run started before leasing holds none of it.
-- The runs are @WorkspaceId's with @TenantId NULL, and every workspace of @TenantId otherwise
-- (dbo.TenantWorkspaces). Each kind keeps its own index path (IX_Runs_WorkspaceId_Active, and
-- IX_RunCounters_Unposted); a caller names the kind it sums, so the other branch folds away. Inline, so
-- it expands into the caller's plan as dbo.RunParked does: the one place these two rules are written.
CREATE FUNCTION dbo.RunCreditsHeld (@WorkspaceId INT, @TenantId INT)
RETURNS TABLE
AS
RETURN
(
    SELECT
        r.Id AS RunId,
        N'Reserved' AS Kind,
        CASE WHEN r.CreditBudget > rc.CreditsConsumed + rc.ChildCreditsConsumed
             THEN r.CreditBudget - rc.CreditsConsumed - rc.ChildCreditsConsumed
             ELSE 0 END AS Credits,
        CASE WHEN rc.CreditAllowance IS NOT NULL THEN 1 ELSE 0 END AS HeldFromAllowance
    FROM dbo.TenantWorkspaces(@WorkspaceId, @TenantId) w
    JOIN dbo.Runs r ON r.WorkspaceId = w.WorkspaceId
    JOIN dbo.RunCounters rc ON rc.RunId = r.Id
    WHERE r.Status IN (N'Running', N'Failing', N'Cancelling')
      AND rc.BudgetRunId IS NULL

    UNION ALL

    SELECT
        r.Id,
        N'Unposted',
        rc.CreditsConsumed - rc.PostedCredits,
        CASE WHEN rc.CreditAllowance IS NOT NULL THEN 1 ELSE 0 END
    FROM dbo.TenantWorkspaces(@WorkspaceId, @TenantId) w
    JOIN dbo.Runs r ON r.WorkspaceId = w.WorkspaceId
    JOIN dbo.RunCounters rc ON rc.RunId = r.Id
    WHERE rc.HasUnposted = 1
);
GO
