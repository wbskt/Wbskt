CREATE PROCEDURE dbo.Permission_Verify
    @UserId INT,
    @PermissionSlug NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PermissionId INT;
    
    -- 1. Get Permission ID
    SELECT @PermissionId = Id FROM dbo.Permissions WHERE Slug = @PermissionSlug;

    -- If permission doesn't exist, strictly deny
    IF @PermissionId IS NULL
    BEGIN
        SELECT 0 AS AccessGranted;
        RETURN;
    END

    -- 2. Check Direct User Permission (Explicit Override)
    DECLARE @UserDeny BIT, @UserAllow BIT;
    
    SELECT 
        @UserDeny = MAX(CASE WHEN IsDeny = 1 THEN 1 ELSE 0 END),
        @UserAllow = MAX(CASE WHEN IsDeny = 0 THEN 1 ELSE 0 END)
    FROM dbo.UserPermissions
    WHERE UserId = @UserId AND PermissionId = @PermissionId;

    -- If User has explicit DENY -> DENY
    IF @UserDeny = 1
    BEGIN
        SELECT 0 AS AccessGranted;
        RETURN;
    END

    -- If User has explicit ALLOW -> ALLOW
    IF @UserAllow = 1
    BEGIN
        SELECT 1 AS AccessGranted;
        RETURN;
    END

    -- 3. Resolve All Roles (Direct + Group Inherited)
    ;WITH AllGroups AS (
        -- Anchor: Direct Groups
        SELECT GroupId FROM dbo.UserGroups WHERE UserId = @UserId
        
        UNION ALL
        
        -- Recursive: Parent Groups
        SELECT g.ParentGroupId
        FROM dbo.Groups g
        INNER JOIN AllGroups ag ON g.Id = ag.GroupId
        WHERE g.ParentGroupId IS NOT NULL
    )
    SELECT DISTINCT r.Id AS RoleId
    INTO #UserEffectiveRoles
    FROM dbo.Roles r
    LEFT JOIN dbo.UserRoles ur ON ur.RoleId = r.Id AND ur.UserId = @UserId
    LEFT JOIN dbo.GroupRoles gr ON gr.RoleId = r.Id
    LEFT JOIN AllGroups ag ON ag.GroupId = gr.GroupId
    WHERE ur.UserId IS NOT NULL OR ag.GroupId IS NOT NULL;

    -- 4. Check Role Permissions
    DECLARE @RoleDeny BIT = 0;
    DECLARE @RoleAllow BIT = 0;

    SELECT 
        @RoleDeny = MAX(CASE WHEN IsDeny = 1 THEN 1 ELSE 0 END),
        @RoleAllow = MAX(CASE WHEN IsDeny = 0 THEN 1 ELSE 0 END)
    FROM dbo.RolePermissions rp
    INNER JOIN #UserEffectiveRoles uer ON uer.RoleId = rp.RoleId
    WHERE rp.PermissionId = @PermissionId;

    -- Clean up temp table
    DROP TABLE #UserEffectiveRoles;

    -- Logic: Any Role DENY -> DENY
    IF @RoleDeny = 1
    BEGIN
        SELECT 0 AS AccessGranted;
        RETURN;
    END

    -- Logic: Any Role ALLOW (and no Deny) -> ALLOW
    IF @RoleAllow = 1
    BEGIN
        SELECT 1 AS AccessGranted;
        RETURN;
    END

    -- Default: DENY
    SELECT 0 AS AccessGranted;
END
