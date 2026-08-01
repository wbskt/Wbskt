-- Detaches a permission from a role definition entirely, as opposed to setting IsDeny.
CREATE PROCEDURE dbo.RolePermission_Remove
    @RoleId INT,
    @PermissionSlug NVARCHAR(100),
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

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

    DELETE FROM dbo.RolePermissions
    WHERE RoleId = @RoleId
      AND PermissionId = @PermissionId;
END
GO
