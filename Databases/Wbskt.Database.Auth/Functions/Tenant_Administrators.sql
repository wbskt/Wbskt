-- The active members of a tenant who hold tenant-wide users.manage, which is what makes someone able
-- to administer the tenant through the API. Every procedure that can take that away checks it before
-- and after its change and refuses (THROW 50008) a change that would leave a tenant that had an
-- administrator with none. Every management endpoint needs tenant-wide users.manage *in that tenant*
-- to act, so nobody would be left who could grant it back.
--
-- Those procedures first take an update lock on the tenant's row in dbo.Tenants and hold it for the
-- transaction. Without it two concurrent removals would each read (under READ_COMMITTED_SNAPSHOT,
-- without blocking) the other administrator as still present, and both commit.
--
-- Precedence follows Docs/AccessControl.md: a user-level DENY beats everything, then a user-level
-- ALLOW, then a role that allows it provided no held role denies it.
--
-- Scope: direct assignments only (UserRoles and UserPermissions). Administrators reached through a
-- group are not counted, so a tenant administered solely through group membership is treated as
-- having none. That is the deliberate direction to be wrong in -- walking the group ancestry here
-- and getting it subtly wrong would let a change orphan the tenant.
--
-- Deactivated accounts and suspended members are not administrators: neither can act in the tenant,
-- so counting them would let the last active administrator remove themselves while one "remained".
CREATE FUNCTION dbo.Tenant_Administrators (@TenantId INT)
RETURNS TABLE
AS
RETURN
    SELECT TM.UserId
    FROM dbo.TenantMembers TM
    INNER JOIN dbo.Users U ON U.Id = TM.UserId
    WHERE TM.TenantId = @TenantId
      AND U.IsActive = 1
      AND TM.IsSuspended = 0

      AND NOT EXISTS (
          SELECT 1
          FROM dbo.UserPermissions UP
          INNER JOIN dbo.Permissions P ON P.Id = UP.PermissionId
          WHERE UP.UserId = TM.UserId
            AND UP.TenantId = @TenantId
            AND UP.WorkspaceId IS NULL
            AND P.Slug = 'users.manage'
            AND UP.IsDeny = 1)

      AND (
          EXISTS (
              SELECT 1
              FROM dbo.UserPermissions UP
              INNER JOIN dbo.Permissions P ON P.Id = UP.PermissionId
              WHERE UP.UserId = TM.UserId
                AND UP.TenantId = @TenantId
                AND UP.WorkspaceId IS NULL
                AND P.Slug = 'users.manage'
                AND UP.IsDeny = 0)

          OR (
              EXISTS (
                  SELECT 1
                  FROM dbo.UserRoles UR
                  INNER JOIN dbo.RolePermissions RP ON RP.RoleId = UR.RoleId
                  INNER JOIN dbo.Permissions P ON P.Id = RP.PermissionId
                  WHERE UR.UserId = TM.UserId
                    AND UR.TenantId = @TenantId
                    AND UR.WorkspaceId IS NULL
                    AND P.Slug = 'users.manage'
                    AND RP.IsDeny = 0)
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.UserRoles UR
                  INNER JOIN dbo.RolePermissions RP ON RP.RoleId = UR.RoleId
                  INNER JOIN dbo.Permissions P ON P.Id = RP.PermissionId
                  WHERE UR.UserId = TM.UserId
                    AND UR.TenantId = @TenantId
                    AND UR.WorkspaceId IS NULL
                    AND P.Slug = 'users.manage'
                    AND RP.IsDeny = 1)));
GO
