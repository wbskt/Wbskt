-- Assigns a role to a group at a scope (@WorkspaceId NULL = tenant-wide). Idempotent.
CREATE PROCEDURE dbo.GroupRole_Assign
    @GroupId INT,
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

    IF NOT EXISTS (SELECT 1 FROM dbo.Groups WHERE Id = @GroupId AND TenantId = @TenantId)
    BEGIN
        THROW 50003, 'Group does not belong to the specified tenant.', 1;
    END

    IF @WorkspaceId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id = @WorkspaceId AND TenantId = @TenantId)
    BEGIN
        THROW 50002, 'Workspace does not belong to the specified tenant.', 1;
    END

    IF NOT EXISTS (
        SELECT 1 FROM dbo.GroupRoles
        WHERE GroupId = @GroupId AND RoleId = @RoleId AND TenantId = @TenantId
          AND ((WorkspaceId IS NULL AND @WorkspaceId IS NULL) OR WorkspaceId = @WorkspaceId)
    )
    BEGIN
        INSERT INTO dbo.GroupRoles (GroupId, RoleId, TenantId, WorkspaceId)
        VALUES (@GroupId, @RoleId, @TenantId, @WorkspaceId);
    END
END
GO
