CREATE PROCEDURE dbo.WorkspaceMember_Add
    @WorkspaceId INT,
    @UserId INT,
    @Role TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId, Role)
    VALUES (@WorkspaceId, @UserId, @Role);
END
GO
