-- Assigns a role to a user at a scope (@WorkspaceId NULL = tenant-wide). Idempotent.
CREATE PROCEDURE dbo.UserRole_Assign
    @UserId INT,
    @RoleId INT,
    @TenantId INT,
    @WorkspaceId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Id = @RoleId AND TenantId = @TenantId)
    BEGIN
        THROW 50001, 'Role does not belong to the specified tenant.', 1;
    END

    IF @WorkspaceId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id = @WorkspaceId AND TenantId = @TenantId)
    BEGIN
        THROW 50002, 'Workspace does not belong to the specified tenant.', 1;
    END

    IF NOT EXISTS (
        SELECT 1 FROM dbo.UserRoles
        WHERE UserId = @UserId AND RoleId = @RoleId AND TenantId = @TenantId
          AND ((WorkspaceId IS NULL AND @WorkspaceId IS NULL) OR WorkspaceId = @WorkspaceId)
    )
    BEGIN
        INSERT INTO dbo.UserRoles (UserId, RoleId, TenantId, WorkspaceId)
        VALUES (@UserId, @RoleId, @TenantId, @WorkspaceId);
    END
END
GO
