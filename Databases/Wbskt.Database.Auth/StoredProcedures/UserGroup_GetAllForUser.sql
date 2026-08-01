CREATE PROCEDURE dbo.UserGroup_GetAllForUser
    @UserId INT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        G.RefId AS GroupRefId,
        G.Name AS GroupName
    FROM dbo.UserGroups UG
    INNER JOIN dbo.Groups G ON G.Id = UG.GroupId
    WHERE UG.UserId = @UserId
      AND G.TenantId = @TenantId
    ORDER BY G.Name;
END
GO
