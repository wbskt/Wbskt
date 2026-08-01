-- The permission catalogue is global and code-defined (Wbskt.Primitives/Constants/Permissions.cs,
-- seeded by Script.PostDeployment.sql), so there is no tenant filter here.
CREATE PROCEDURE dbo.Permission_GetAll
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.Permissions;

    SELECT
        Slug,
        Description
    FROM dbo.Permissions
    ORDER BY Slug
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO
