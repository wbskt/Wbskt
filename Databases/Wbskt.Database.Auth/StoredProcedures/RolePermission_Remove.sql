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

    DELETE FROM dbo.RolePermissions
    WHERE RoleId = @RoleId
      AND PermissionId = @PermissionId;

    IF @HadAdministrator = 1 AND NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId))
    BEGIN
        THROW 50008, 'This change would leave the tenant without an administrator.', 1;
    END

    COMMIT TRANSACTION;
END
GO
