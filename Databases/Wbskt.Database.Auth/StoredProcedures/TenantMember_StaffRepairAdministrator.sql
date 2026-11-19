-- Makes a member an administrator of the tenant for staff, when nobody left in it can: assigns the
-- tenant's Admin role tenant-wide and removes the member's own tenant-wide denies of users.manage and
-- roles.manage, which would otherwise beat the role (see dbo.Tenant_Administrators). Returns the Admin
-- role's RefId. Only for a tenant nobody can administer any more.
--
-- 50016: the person is not a member of this tenant.
-- 50048: the member is suspended here or their sign-in is off, so they could not act as administrator.
-- 50050: the tenant still has a working administrator (dbo.Tenant_Administrators), so it needs no repair;
--        staff must not hand a customer's administration to someone its own administrators did not pick.
CREATE PROCEDURE dbo.TenantMember_StaffRepairAdministrator
    @TenantId INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- The same tenant lock the last-administrator guards take, so this cannot interleave with them.
    DECLARE @TenantLock INT;
    SELECT @TenantLock = Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TenantId;

    DECLARE @IsSuspended BIT;
    DECLARE @IsActive BIT;
    SELECT
        @IsSuspended = TM.IsSuspended,
        @IsActive = U.IsActive
    FROM dbo.TenantMembers TM
    INNER JOIN dbo.Users U ON U.Id = TM.UserId
    WHERE TM.TenantId = @TenantId
      AND TM.UserId = @UserId;

    IF @IsSuspended IS NULL
    BEGIN
        THROW 50016, 'The user is not a member of this tenant.', 1;
    END

    IF @IsSuspended = 1 OR @IsActive = 0
    BEGIN
        THROW 50048, 'The member is suspended or cannot sign in.', 1;
    END

    IF EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId))
    BEGIN
        THROW 50050, 'The tenant still has an administrator.', 1;
    END

    DECLARE @AdminRoleId INT;
    DECLARE @AdminRoleRef UNIQUEIDENTIFIER;
    SELECT
        @AdminRoleId = Id,
        @AdminRoleRef = RefId
    FROM dbo.Roles
    WHERE TenantId = @TenantId
      AND Kind = N'Admin';

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.UserRoles
        WHERE UserId = @UserId
          AND RoleId = @AdminRoleId
          AND TenantId = @TenantId
          AND WorkspaceId IS NULL)
    BEGIN
        INSERT INTO dbo.UserRoles (UserId, RoleId, TenantId, WorkspaceId)
        VALUES (@UserId, @AdminRoleId, @TenantId, NULL);
    END

    DELETE UP
    FROM dbo.UserPermissions UP
    INNER JOIN dbo.Permissions P ON P.Id = UP.PermissionId
    WHERE UP.UserId = @UserId
      AND UP.TenantId = @TenantId
      AND UP.WorkspaceId IS NULL
      AND UP.IsDeny = 1
      AND P.Slug IN (N'users.manage', N'roles.manage');

    COMMIT TRANSACTION;

    SELECT @AdminRoleRef;
END
GO
