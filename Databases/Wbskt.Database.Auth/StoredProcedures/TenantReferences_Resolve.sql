-- Resolves the public references a tenant administration write names to their ids, inside the tenant
-- only, so another tenant's user, role or workspace reads the same as one that does not exist. Each
-- reference left NULL is skipped and its id comes back NULL. One that names nothing throws its own
-- number, which the auth host's SqlGuardErrors reads back as that resource's 404: 50037 for a user
-- who is not a member (USER_NOT_FOUND), 50038 for a role (ROLE_NOT_FOUND) and 50017 for a workspace
-- (WORKSPACE_NOT_FOUND). Lets the write resolve and act in one round trip instead of one per reference.
CREATE PROCEDURE dbo.TenantReferences_Resolve
    @TenantId INT,
    @UserRefId UNIQUEIDENTIFIER = NULL,
    @RoleRefId UNIQUEIDENTIFIER = NULL,
    @WorkspaceRefId UNIQUEIDENTIFIER = NULL,
    @UserId INT = NULL OUTPUT,
    @RoleId INT = NULL OUTPUT,
    @WorkspaceId INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SET @UserId = NULL;
    SET @RoleId = NULL;
    SET @WorkspaceId = NULL;

    IF @UserRefId IS NOT NULL
    BEGIN
        SELECT @UserId = U.Id
        FROM dbo.Users U
        INNER JOIN dbo.TenantMembers TM ON TM.UserId = U.Id
        WHERE U.RefId = @UserRefId
          AND TM.TenantId = @TenantId;

        IF @UserId IS NULL
        BEGIN
            THROW 50037, 'User not found in this tenant.', 1;
        END
    END

    IF @RoleRefId IS NOT NULL
    BEGIN
        SELECT @RoleId = Id
        FROM dbo.Roles
        WHERE RefId = @RoleRefId
          AND TenantId = @TenantId;

        IF @RoleId IS NULL
        BEGIN
            THROW 50038, 'Role not found in this tenant.', 1;
        END
    END

    IF @WorkspaceRefId IS NOT NULL
    BEGIN
        SELECT @WorkspaceId = Id
        FROM dbo.Workspaces
        WHERE RefId = @WorkspaceRefId
          AND TenantId = @TenantId;

        IF @WorkspaceId IS NULL
        BEGIN
            THROW 50017, 'Workspace not found in this tenant.', 1;
        END
    END
END
GO
