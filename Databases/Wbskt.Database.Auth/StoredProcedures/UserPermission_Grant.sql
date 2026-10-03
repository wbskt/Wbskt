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
    SET XACT_ABORT ON;

    DECLARE @PermissionId INT;
    SELECT @PermissionId = Id FROM dbo.Permissions WHERE Slug = @PermissionSlug;

    -- An unknown slug is a caller mistake (a typo grants nothing), so it is rejected rather than
    -- silently skipped. Without this the API answers 204 for a grant that never happened.
    IF @PermissionId IS NULL
    BEGIN
        THROW 50007, 'Permission slug does not exist.', 1;
    END

    IF @WorkspaceId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id = @WorkspaceId AND TenantId = @TenantId)
    BEGIN
        THROW 50002, 'Workspace does not belong to the specified tenant.', 1;
    END

    BEGIN TRANSACTION;

    -- Last-administrator guard; see dbo.Tenant_Administrators for the rule and the lock.
    DECLARE @TenantLock INT;
    SELECT @TenantLock = Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TenantId;
    DECLARE @HadAdministrator BIT = IIF(EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId)), 1, 0);

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

    IF @HadAdministrator = 1 AND NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId))
    BEGIN
        THROW 50008, 'This change would leave the tenant without an administrator.', 1;
    END

    COMMIT TRANSACTION;
END
GO
