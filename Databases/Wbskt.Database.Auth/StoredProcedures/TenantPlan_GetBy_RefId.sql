CREATE PROCEDURE dbo.TenantPlan_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        RefId,
        PlanCode,
        PlanLimitOverrides,
        PlanRevision,
        PlanChangedAt
    FROM dbo.Tenants
    WHERE RefId = @RefId;
END
GO
