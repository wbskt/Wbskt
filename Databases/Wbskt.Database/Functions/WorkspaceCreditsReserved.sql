-- The credits @WorkspaceId's runs still going may yet spend: each run with its own budget's unspent part
-- (its budget less what it and the sub-workflow children sharing it consumed). With
-- dbo.WorkspaceCreditsSpent, what Run_Start holds a workspace's credit cap to, so runs going at once can
-- never spend past it between them.
CREATE FUNCTION dbo.WorkspaceCreditsReserved (@WorkspaceId INT)
RETURNS TABLE
AS
RETURN
(
    SELECT ISNULL(SUM(CASE WHEN r.CreditBudget > rc.CreditsConsumed + rc.ChildCreditsConsumed
                           THEN r.CreditBudget - rc.CreditsConsumed - rc.ChildCreditsConsumed
                           ELSE 0 END), 0) AS Credits
    FROM dbo.WorkflowDefinitions d
    JOIN dbo.Runs r ON r.WorkflowDefinitionId = d.Id
    JOIN dbo.RunCounters rc ON rc.RunId = r.Id
    WHERE d.WorkspaceId = @WorkspaceId
      AND r.Status IN (N'Running', N'Failing', N'Cancelling')
      AND rc.BudgetRunId IS NULL
);
GO
