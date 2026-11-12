-- The credits the runs going in @TenantId's workspaces (dbo.TenantWorkspaces) may yet spend of the plan's
-- monthly credits: each root run leased from them (dbo.RunCounters.CreditAllowance set by Run_Start) with
-- its budget's unspent part, as dbo.WorkspaceCreditsReserved counts them against a workspace's cap. With
-- what was spent, what Run_Start and RunCounters_TryCharge hold the tenant's allowance to
-- (dbo.TenantCreditsLeft), so runs going at once can never spend past it between them. A run started
-- before budgets were leased from the allowance holds none of it.
CREATE FUNCTION dbo.TenantCreditsReserved (@WorkspaceId INT, @TenantId INT)
RETURNS TABLE
AS
RETURN
(
    SELECT ISNULL(SUM(CASE WHEN r.CreditBudget > rc.CreditsConsumed + rc.ChildCreditsConsumed
                           THEN r.CreditBudget - rc.CreditsConsumed - rc.ChildCreditsConsumed
                           ELSE 0 END), 0) AS Credits
    FROM dbo.WorkflowDefinitions d
    JOIN dbo.TenantWorkspaces(@WorkspaceId, @TenantId) w ON w.WorkspaceId = d.WorkspaceId
    JOIN dbo.Runs r ON r.WorkflowDefinitionId = d.Id
    JOIN dbo.RunCounters rc ON rc.RunId = r.Id
    WHERE r.Status IN (N'Running', N'Failing', N'Cancelling')
      AND rc.BudgetRunId IS NULL
      AND rc.CreditAllowance IS NOT NULL
);
GO
