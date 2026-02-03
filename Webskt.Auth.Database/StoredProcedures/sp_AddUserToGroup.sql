CREATE PROCEDURE [dbo].[sp_AddUserToGroup]
    @UserId INT,
    @GroupId INT
AS
BEGIN
    INSERT INTO [dbo].[UserGroups] (UserId, GroupId)
    VALUES (@UserId, @GroupId);
END
