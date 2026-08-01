CREATE PROCEDURE dbo.UserGroup_Remove
    @UserId INT,
    @GroupId INT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Groups WHERE Id = @GroupId AND TenantId = @TenantId)
    BEGIN
        THROW 50003, 'Group does not belong to the specified tenant.', 1;
    END

    DELETE FROM dbo.UserGroups
    WHERE UserId = @UserId
      AND GroupId = @GroupId;
END
GO
