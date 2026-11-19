-- Assigns a role to a user at a scope (@WorkspaceId NULL = tenant-wide). Idempotent.
CREATE PROCEDURE dbo.UserRole_Assign
    @UserId INT = NULL,
    @RoleId INT = NULL,
    @TenantId INT,
    @WorkspaceId INT = NULL,
    -- The public references, resolved here within @TenantId (dbo.TenantReferences_Resolve) in place
    -- of the ids above, so the caller needs no lookup first. The ids stay for the host still running
    -- during a rollout; dropped in the release after (#1059).
    @UserRefId UNIQUEIDENTIFIER = NULL,
    @RoleRefId UNIQUEIDENTIFIER = NULL,
    @WorkspaceRefId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @UserRefId IS NOT NULL
    BEGIN
        EXEC dbo.TenantReferences_Resolve
            @TenantId = @TenantId,
            @UserRefId = @UserRefId,
            @RoleRefId = @RoleRefId,
            @WorkspaceRefId = @WorkspaceRefId,
            @UserId = @UserId OUTPUT,
            @RoleId = @RoleId OUTPUT,
            @WorkspaceId = @WorkspaceId OUTPUT;
    END

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
