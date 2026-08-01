-- RolePermissions are part of the role definition and carry no scope of their own.
CREATE PROCEDURE dbo.RolePermission_GetAllForRole
    @RoleId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        P.Slug,
        RP.IsDeny
    FROM dbo.RolePermissions RP
    INNER JOIN dbo.Permissions P ON P.Id = RP.PermissionId
    WHERE RP.RoleId = @RoleId
    ORDER BY P.Slug;
END
GO
