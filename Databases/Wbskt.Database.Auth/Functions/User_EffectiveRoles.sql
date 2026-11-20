-- The roles a user holds in a tenant: assigned to them, or through a group they are in and its ancestors
-- when @ThroughGroups is 1. An assignment counts when it is tenant-wide (WorkspaceId NULL) or, when
-- @WorkspaceId is given, scoped to that workspace. dbo.User_EffectivePermissions turns these into
-- permissions; the workspaces list reads whether one of them is the tenant's Administrator role.
CREATE FUNCTION dbo.User_EffectiveRoles (@UserId INT, @TenantId INT, @WorkspaceId INT, @ThroughGroups BIT)
RETURNS TABLE
AS
RETURN
    WITH AllGroups AS (
        SELECT UG.GroupId
        FROM dbo.UserGroups UG
        INNER JOIN dbo.Groups G ON G.Id = UG.GroupId AND G.TenantId = @TenantId
        WHERE UG.UserId = @UserId
          AND @ThroughGroups = 1

        UNION ALL

        SELECT G.ParentGroupId
        FROM dbo.Groups G
        INNER JOIN AllGroups AG ON G.Id = AG.GroupId
        WHERE G.ParentGroupId IS NOT NULL
    ),
    EffectiveRoles AS (
        SELECT UR.RoleId
        FROM dbo.UserRoles UR
        WHERE UR.UserId = @UserId
          AND UR.TenantId = @TenantId
          AND (UR.WorkspaceId IS NULL OR UR.WorkspaceId = @WorkspaceId)

        UNION

        SELECT GR.RoleId
        FROM dbo.GroupRoles GR
        INNER JOIN AllGroups AG ON AG.GroupId = GR.GroupId
        WHERE GR.TenantId = @TenantId
          AND (GR.WorkspaceId IS NULL OR GR.WorkspaceId = @WorkspaceId)
    )
    SELECT RoleId FROM EffectiveRoles;
GO
