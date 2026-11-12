-- Whether a member of @GroupId would hold any role through it: a role assigned to the group itself or
-- to any group above it, since members inherit their ancestors' roles (dbo.Permission_Verify). Adding
-- someone to such a group grants roles, so it needs roles.manage as well as users.manage.
-- The walk stops at the root: a group's parent must already exist, so the chain has no cycle, and
-- nesting stays well inside the default MAXRECURSION of 100.
CREATE PROCEDURE dbo.GroupRole_Verify
    @GroupId INT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH Lineage AS (
        SELECT g.Id, g.ParentGroupId
        FROM dbo.Groups g
        WHERE g.Id = @GroupId
          AND g.TenantId = @TenantId

        UNION ALL

        SELECT p.Id, p.ParentGroupId
        FROM dbo.Groups p
        INNER JOIN Lineage l ON p.Id = l.ParentGroupId
    )
    SELECT CAST(IIF(EXISTS (
        SELECT 1
        FROM dbo.GroupRoles gr
        INNER JOIN Lineage l ON l.Id = gr.GroupId), 1, 0) AS BIT) AS CarriesRoles;
END
GO
