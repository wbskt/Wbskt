-- Removes a role assignment from a user at a scope (@WorkspaceId NULL = tenant-wide).
CREATE PROCEDURE dbo.UserRole_Remove
    @UserId INT = NULL,
    @RoleId INT = NULL,
    @TenantId INT,
    @WorkspaceId INT = NULL,
    -- The public references, resolved here within @TenantId (dbo.TenantReferences_Resolve) in place
    -- of the ids above, so the caller needs no lookup first. The ids stay for the host still running
    -- during a rollout; remove them once no caller passes them.
    @UserRefId UNIQUEIDENTIFIER = NULL,
    @RoleRefId UNIQUEIDENTIFIER = NULL,
    @WorkspaceRefId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

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

    BEGIN TRANSACTION;

    -- Last-administrator guard; see dbo.Tenant_Administrators for the rule and the lock.
    DECLARE @HadAdministrator BIT;
    EXEC dbo.Tenant_LockAdministered @TenantId = @TenantId, @HadAdministrator = @HadAdministrator OUTPUT;

    DELETE FROM dbo.UserRoles
    WHERE UserId = @UserId AND RoleId = @RoleId AND TenantId = @TenantId
      AND ((WorkspaceId IS NULL AND @WorkspaceId IS NULL) OR WorkspaceId = @WorkspaceId);

    EXEC dbo.Tenant_AssertAdministered @TenantId = @TenantId, @HadAdministrator = @HadAdministrator;

    COMMIT TRANSACTION;
END
GO
