-- Returns every permission slug the user effectively holds in the given workspace: assignments in the
-- workspace's tenant that are tenant-wide or scoped to this workspace, by dbo.User_EffectivePermissions
-- (which holds the precedence rule and the suspension). Nothing for a workspace that does not exist.
CREATE PROCEDURE dbo.Permission_EffectiveSet
    @UserId INT,
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Workspaces WHERE Id = @WorkspaceId);

    SELECT Slug
    FROM dbo.User_EffectivePermissions(@UserId, @TenantId, @WorkspaceId, 1);
END
GO
