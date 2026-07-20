-- Removes a role assignment from a user at a scope (@WorkspaceId NULL = tenant-wide).
CREATE PROCEDURE dbo.UserRole_Remove
    @UserId INT,
    @RoleId INT,
    @TenantId INT,
    @WorkspaceId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.UserRoles
    WHERE UserId = @UserId AND RoleId = @RoleId AND TenantId = @TenantId
      AND ((WorkspaceId IS NULL AND @WorkspaceId IS NULL) OR WorkspaceId = @WorkspaceId);
END
GO
