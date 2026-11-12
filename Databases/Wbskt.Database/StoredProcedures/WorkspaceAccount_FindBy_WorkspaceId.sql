-- The account copy of a workspace still in use. A deleted workspace's copy (RetiredAt) is kept only
-- so its month's usage stays on the tenant's meters, and is not found here.
CREATE PROCEDURE dbo.WorkspaceAccount_FindBy_WorkspaceId
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        WorkspaceId,
        TenantId,
        TenantRefId,
        PlanCode,
        LimitOverrides,
        PlanRevision
    FROM dbo.WorkspaceAccounts
    WHERE WorkspaceId = @WorkspaceId
      AND RetiredAt IS NULL;
END
GO
