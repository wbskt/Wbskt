-- Grants or denies a permission directly to a user at a scope (@WorkspaceId NULL = tenant-wide).
CREATE PROCEDURE dbo.UserPermission_Grant
    @UserId INT,
    @PermissionSlug NVARCHAR(100),
    @IsDeny BIT,
    @TenantId INT,
    @WorkspaceId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PermissionId INT;
    SELECT @PermissionId = Id FROM dbo.Permissions WHERE Slug = @PermissionSlug;

    IF @PermissionId IS NOT NULL
    BEGIN
        IF @WorkspaceId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id = @WorkspaceId AND TenantId = @TenantId)
        BEGIN
            THROW 50002, 'Workspace does not belong to the specified tenant.', 1;
        END

        MERGE dbo.UserPermissions AS target
        USING (SELECT @UserId AS UserId, @PermissionId AS PermissionId) AS source
        ON (target.UserId = source.UserId
            AND target.PermissionId = source.PermissionId
            AND target.TenantId = @TenantId
            AND ((target.WorkspaceId IS NULL AND @WorkspaceId IS NULL) OR target.WorkspaceId = @WorkspaceId))
        WHEN MATCHED THEN
            UPDATE SET IsDeny = @IsDeny
        WHEN NOT MATCHED THEN
            INSERT (
                UserId,
                PermissionId,
                TenantId,
                WorkspaceId,
                IsDeny
            )
            VALUES (
                @UserId,
                @PermissionId,
                @TenantId,
                @WorkspaceId,
                @IsDeny
            );
    END
END
GO
