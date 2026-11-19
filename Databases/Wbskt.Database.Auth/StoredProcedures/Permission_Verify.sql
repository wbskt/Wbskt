-- Verifies a single permission for a user within a scope, by dbo.User_EffectivePermissions (which holds
-- the precedence rule and the suspension).
-- @WorkspaceId NULL: only tenant-wide assignments count (used for tenant-level management checks).
-- @WorkspaceId set: tenant-wide + workspace-scoped assignments count.
CREATE PROCEDURE dbo.Permission_Verify
    @UserId INT,
    @TenantId INT,
    @WorkspaceId INT = NULL,
    @PermissionSlug NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CASE
               WHEN EXISTS (
                   SELECT 1
                   FROM dbo.User_EffectivePermissions(@UserId, @TenantId, @WorkspaceId, 1)
                   WHERE Slug = @PermissionSlug) THEN 1
               ELSE 0
           END AS AccessGranted;
END
GO
