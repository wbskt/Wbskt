-- Detaches a permission from a role definition entirely, as opposed to setting IsDeny.
CREATE PROCEDURE dbo.RolePermission_Remove
    @RoleId INT,
    @PermissionSlug NVARCHAR(100),
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Id = @RoleId AND TenantId = @TenantId)
    BEGIN
        THROW 50001, 'Role does not belong to the specified tenant.', 1;
    END

    -- The Admin role holds every permission, so a tenant can always be administered: taking one away
    -- (roles.manage, say) could leave nobody able to give it back.
    IF EXISTS (SELECT 1 FROM dbo.Roles WHERE Id = @RoleId AND Kind = N'Admin')
    BEGIN
        THROW 50028, 'The built-in Admin role holds every permission and cannot be changed.', 1;
    END

    DECLARE @PermissionId INT;
    SELECT @PermissionId = Id FROM dbo.Permissions WHERE Slug = @PermissionSlug;

    IF @PermissionId IS NULL
    BEGIN
        RETURN;
    END

    BEGIN TRANSACTION;

    -- Last-administrator guard; see dbo.Tenant_Administrators for the rule and the lock.
    DECLARE @HadAdministrator BIT;
    EXEC dbo.Tenant_LockAdministered @TenantId = @TenantId, @HadAdministrator = @HadAdministrator OUTPUT;

    DELETE FROM dbo.RolePermissions
    WHERE RoleId = @RoleId
      AND PermissionId = @PermissionId;

    EXEC dbo.Tenant_AssertAdministered @TenantId = @TenantId, @HadAdministrator = @HadAdministrator;

    COMMIT TRANSACTION;
END
GO
