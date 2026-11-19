-- The active members of a tenant who hold both users.manage and roles.manage tenant-wide, which is what
-- makes someone able to administer the tenant through the API: users.manage to add, remove and suspend
-- members, roles.manage to create, assign and change roles and to invite with one. Every procedure that
-- can take either away checks this before and after its change and refuses (THROW 50008) a change that
-- would leave a tenant that had an administrator with none, since nobody would be left who could grant
-- it back. Counting users.manage alone let the only holder of roles.manage deny it to themselves, or be
-- removed by someone holding users.manage through a custom role, and lose it for good (#948).
--
-- Those procedures first take an update lock on the tenant's row in dbo.Tenants and hold it for the
-- transaction. Without it two concurrent removals would each read (under READ_COMMITTED_SNAPSHOT,
-- without blocking) the other administrator as still present, and both commit.
--
-- Whether a member holds each of the two is dbo.User_EffectivePermissions, the same precedence every
-- permission check uses, over tenant-wide assignments only.
--
-- Scope: direct assignments only (UserRoles and UserPermissions, @ThroughGroups = 0). Administrators
-- reached through a group are not counted, so a tenant administered solely through group membership is
-- treated as having none. That is the deliberate direction to be wrong in: a change to a group or its
-- ancestry would otherwise need the same guard, and missing one would let it orphan the tenant.
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
      AND (SELECT COUNT(*)
           FROM dbo.User_EffectivePermissions(TM.UserId, @TenantId, NULL, 0) E
           WHERE E.Slug IN (N'users.manage', N'roles.manage')) = 2;
GO
