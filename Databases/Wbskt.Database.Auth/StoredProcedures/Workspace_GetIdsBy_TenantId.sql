-- Every workspace in a tenant, for the audit log: an action on the tenant's people (an invitation, a
-- suspension, a role change) is logged in each of its workspaces, since the log is read per workspace.
CREATE PROCEDURE dbo.Workspace_GetIdsBy_TenantId
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id FROM dbo.Workspaces WHERE TenantId = @TenantId;
END
GO
