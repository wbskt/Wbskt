-- The credits the runs going in @TenantId's workspaces (dbo.TenantWorkspaces) may yet spend of the plan's
-- monthly credits: the Reserved credits of the runs that leased from them (dbo.RunCreditsHeld). With
-- what was spent, what Run_Start and RunCounters_TryCharge hold the tenant's allowance to
-- (dbo.TenantCreditsLeft), so runs going at once can never spend past it between them.
CREATE FUNCTION dbo.TenantCreditsReserved (@WorkspaceId INT, @TenantId INT)
RETURNS TABLE
AS
RETURN
(
    SELECT ISNULL(SUM(h.Credits), 0) AS Credits
    FROM dbo.RunCreditsHeld(@WorkspaceId, @TenantId) h
    WHERE h.Kind = N'Reserved'
      AND h.HeldFromAllowance = 1
);
GO
