CREATE PROCEDURE dbo.RolePermission_Grant
    @RoleId INT,
    @PermissionSlug NVARCHAR(100),
    @IsDeny BIT,
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

    -- An unknown slug is a caller mistake (a typo grants nothing), so it is rejected rather than
    -- silently skipped. Without this the API answers 204 for a grant that never happened.
    IF @PermissionId IS NULL
    BEGIN
        THROW 50007, 'Permission slug does not exist.', 1;
    END

    MERGE dbo.RolePermissions AS target
    USING (SELECT @RoleId AS RoleId, @PermissionId AS PermissionId) AS source
    ON (target.RoleId = source.RoleId AND target.PermissionId = source.PermissionId)
    WHEN MATCHED THEN
        UPDATE SET IsDeny = @IsDeny
    WHEN NOT MATCHED THEN
        INSERT (
            RoleId,
            PermissionId,
            IsDeny
        )
        VALUES (
            @RoleId,
            @PermissionId,
            @IsDeny
        );
END
GO
