-- The tenants with credit grants that could have paid for the month starting @MonthStart (not revoked
-- before it, not expired before it, credits left) and whose month is not settled yet, with the plan
-- copy the allowance is worked out from.
CREATE PROCEDURE dbo.TenantCreditGrants_GetUnsettled
    @MonthStart DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        g.TenantId,
        a.PlanCode,
        a.LimitOverrides
    FROM (
        SELECT DISTINCT TenantId
        FROM dbo.TenantCreditGrants
        WHERE ExpiresAt > @MonthStart
          AND GrantedAt < DATEADD(MONTH, 1, @MonthStart)
          AND (RevokedAt IS NULL OR RevokedAt > @MonthStart)
          AND Drawn < Credits
    ) g
    OUTER APPLY (
        SELECT TOP (1) PlanCode, LimitOverrides
        FROM dbo.WorkspaceAccounts
        WHERE TenantId = g.TenantId
        ORDER BY PlanRevision DESC
    ) a
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.TenantCreditGrantSettlements s
        WHERE s.TenantId = g.TenantId
          AND s.MonthStart = @MonthStart);
END
GO
