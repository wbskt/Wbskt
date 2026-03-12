CREATE PROCEDURE dbo.UserGroup_Insert
    @UserId INT,
    @GroupId INT
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO dbo.UserGroups (
        UserId, 
        GroupId
    )
    VALUES (
        @UserId, 
        @GroupId
    );
END
GO
