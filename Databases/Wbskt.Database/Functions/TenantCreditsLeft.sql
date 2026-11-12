-- What @TenantId's runs may still be given of the plan's workflow credits for the calendar month (UTC)
-- holding @At, with @Allowance the plan's credits a month (on Business, its overage multiple of them):
-- what the plan itself has not covered yet (the ledger less what was drawn from credit grants as it was
-- posted, dbo.TenantCreditMonths; none once a downgrade puts the month past it), plus what the grants
-- still hold, less what runs consumed and have not posted yet, ended or not, and less what the runs
-- going hold of their budgets (dbo.TenantCreditsReserved). One statement, so a post moving credits
-- from unposted to posted cannot be missed by both or counted by both (one snapshot under RCSI). Can be
-- negative: a run that started before leasing, or a downgrade, can put the month past it.
CREATE FUNCTION dbo.TenantCreditsLeft (@WorkspaceId INT, @TenantId INT, @Allowance DECIMAL(18,4), @At DATETIME2(3))
RETURNS TABLE
AS
RETURN
(
    SELECT
        CASE WHEN p.PlanLeft > 0 THEN p.PlanLeft ELSE 0 END
      - p.Unposted
      + p.Grants
      - p.Reserved AS Credits
    FROM (SELECT
              @Allowance
            - ISNULL((SELECT SUM(u.Credits)
                      FROM dbo.WorkspaceCreditUsage u
                      JOIN dbo.TenantWorkspaces(@WorkspaceId, @TenantId) w ON w.WorkspaceId = u.WorkspaceId
                      WHERE u.Day >= DATEFROMPARTS(YEAR(@At), MONTH(@At), 1)), 0)
            + ISNULL((SELECT m.Drawn
                      FROM dbo.TenantCreditMonths m
                      WHERE m.TenantId = @TenantId
                        AND m.MonthStart = DATEFROMPARTS(YEAR(@At), MONTH(@At), 1)), 0) AS PlanLeft,
            ISNULL((SELECT SUM(rc.CreditsConsumed - rc.PostedCredits)
                    FROM dbo.RunCounters rc
                    JOIN dbo.Runs r ON r.Id = rc.RunId
                    JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
                    JOIN dbo.TenantWorkspaces(@WorkspaceId, @TenantId) w ON w.WorkspaceId = d.WorkspaceId
                    WHERE rc.HasUnposted = 1), 0) AS Unposted,
            CASE WHEN @TenantId IS NULL THEN 0
                 ELSE (SELECT g.Credits FROM dbo.TenantCreditGrantBalance(@TenantId, @At) g) END AS Grants,
            (SELECT x.Credits FROM dbo.TenantCreditsReserved(@WorkspaceId, @TenantId) x) AS Reserved
         ) p
);
GO
