-- One workspace with its tenant's plan, by the workspace's RefId, in the shape of
-- dbo.WorkspaceAccount_GetPage. For the management host, which keys a workspace by its Id and has to
-- learn it before acting on a RefId it was given. No row when there is no such workspace.
CREATE PROCEDURE dbo.WorkspaceAccount_FindBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        w.Id AS WorkspaceId,
        t.Id AS TenantId,
        t.RefId AS TenantRefId,
        t.PlanCode,
        t.PlanLimitOverrides,
        t.PlanRevision
    FROM dbo.Workspaces w
    JOIN dbo.Tenants t ON t.Id = w.TenantId
    WHERE w.RefId = @RefId;
END
GO
