CREATE PROCEDURE dbo.TenantPlan_GetBy_Id
    @Id INT
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
    WHERE Id = @Id;
END
GO
