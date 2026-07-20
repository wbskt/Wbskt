CREATE PROCEDURE dbo.WorkspaceMember_Add
    @WorkspaceId INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMembers WHERE WorkspaceId = @WorkspaceId AND UserId = @UserId)
    BEGIN
        INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
        VALUES (@WorkspaceId, @UserId);
    END
END
GO
