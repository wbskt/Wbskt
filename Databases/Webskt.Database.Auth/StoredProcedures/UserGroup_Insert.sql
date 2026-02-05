CREATE PROCEDURE dbo.UserGroup_Insert
    @UserId INT,
    @GroupId INT
AS
BEGIN
    INSERT INTO dbo.UserGroups (
        UserId, 
        GroupId
    )
    VALUES (
        @UserId, 
        @GroupId
    );
END
