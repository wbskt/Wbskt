-- The tenants on Business now: each tenant's latest plan copy (dbo.WorkspaceAccounts, the highest
-- PlanRevision of its workspaces not deleted), with its contract and when that copy last changed.
CREATE FUNCTION dbo.BusinessTenantsNow ()
RETURNS TABLE
AS
RETURN
(
    SELECT
        l.TenantId,
        l.TenantRefId,
        l.LimitOverrides,
        l.UpdatedAt
    FROM (SELECT
              TenantId,
              TenantRefId,
              PlanCode,
              LimitOverrides,
              UpdatedAt,
              ROW_NUMBER() OVER (PARTITION BY TenantId ORDER BY PlanRevision DESC, WorkspaceId) AS Pick
          FROM dbo.WorkspaceAccounts
          -- A deleted workspace's copy stops following the tenant's plan, so it is not read for the plan.
          WHERE RetiredAt IS NULL) l
    WHERE l.Pick = 1
      AND l.PlanCode = N'Business'
);
GO
