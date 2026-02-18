CREATE PROCEDURE dbo.WorkspaceMember_Verify
    @UserId INT,
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Role
    FROM dbo.WorkspaceMembers
    WHERE UserId = @UserId AND WorkspaceId = @WorkspaceId;
END
GO
