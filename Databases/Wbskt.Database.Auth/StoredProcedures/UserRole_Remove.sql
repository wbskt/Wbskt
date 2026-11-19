-- Removes a role assignment from a user at a scope (@WorkspaceId NULL = tenant-wide).
CREATE PROCEDURE dbo.UserRole_Remove
    @UserId INT,
    @RoleId INT,
    @TenantId INT,
    @WorkspaceId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

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
