-- The application lock a pooled plan limit is counted under (sp_getapplock): one per tenant, so
-- concurrent starts or registrations in any of its workspaces take turns, or one per workspace while
-- its tenant is not known yet. @Limit names the limit ('clients', 'runs').
CREATE FUNCTION dbo.PlanLimitLockResource (@Limit NVARCHAR(20), @WorkspaceId INT, @TenantId INT)
RETURNS NVARCHAR(255)
WITH SCHEMABINDING
AS
BEGIN
    RETURN CASE
        WHEN @TenantId IS NULL THEN CONCAT(N'plan-limit:', @Limit, N':workspace:', @WorkspaceId)
        ELSE CONCAT(N'plan-limit:', @Limit, N':tenant:', @TenantId)
    END;
END
GO
