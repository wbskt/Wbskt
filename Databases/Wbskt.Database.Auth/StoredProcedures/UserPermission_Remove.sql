-- Removes a direct user permission at a scope, returning to "no assignment" rather than flipping
-- allow to deny. This distinction matters: a user-level row wins over any role-derived permission,
-- so without a remove an accidental grant can never be undone - denying it is not the same as
-- letting the user's roles decide again.
CREATE PROCEDURE dbo.UserPermission_Remove
    @UserId INT,
    @PermissionSlug NVARCHAR(100),
    @TenantId INT,
    @WorkspaceId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PermissionId INT;
    SELECT @PermissionId = Id FROM dbo.Permissions WHERE Slug = @PermissionSlug;

    IF @PermissionId IS NULL
    BEGIN
        RETURN;
    END

    DELETE FROM dbo.UserPermissions
    WHERE UserId = @UserId
      AND PermissionId = @PermissionId
      AND TenantId = @TenantId
      AND ((WorkspaceId IS NULL AND @WorkspaceId IS NULL) OR WorkspaceId = @WorkspaceId);
END
GO
