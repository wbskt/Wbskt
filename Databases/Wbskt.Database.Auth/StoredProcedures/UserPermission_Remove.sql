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
    SET XACT_ABORT ON;

    DECLARE @PermissionId INT;
    SELECT @PermissionId = Id FROM dbo.Permissions WHERE Slug = @PermissionSlug;

    IF @PermissionId IS NULL
    BEGIN
        RETURN;
    END

    BEGIN TRANSACTION;

    -- Last-administrator guard; see dbo.Tenant_Administrators for the rule and the lock.
    DECLARE @TenantLock INT;
    SELECT @TenantLock = Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TenantId;
    DECLARE @HadAdministrator BIT = IIF(EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId)), 1, 0);

    DELETE FROM dbo.UserPermissions
    WHERE UserId = @UserId
      AND PermissionId = @PermissionId
      AND TenantId = @TenantId
      AND ((WorkspaceId IS NULL AND @WorkspaceId IS NULL) OR WorkspaceId = @WorkspaceId);

    IF @HadAdministrator = 1 AND NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId))
    BEGIN
        THROW 50008, 'This change would leave the tenant without an administrator.', 1;
    END

    COMMIT TRANSACTION;
END
GO
