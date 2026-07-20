-- Removes a role assignment from a group at a scope (@WorkspaceId NULL = tenant-wide).
CREATE PROCEDURE dbo.GroupRole_Remove
    @GroupId INT,
    @RoleId INT,
    @TenantId INT,
    @WorkspaceId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.GroupRoles
    WHERE GroupId = @GroupId AND RoleId = @RoleId AND TenantId = @TenantId
      AND ((WorkspaceId IS NULL AND @WorkspaceId IS NULL) OR WorkspaceId = @WorkspaceId);
END
GO
