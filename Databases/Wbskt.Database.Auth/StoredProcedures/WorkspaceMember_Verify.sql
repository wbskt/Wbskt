CREATE PROCEDURE dbo.WorkspaceMember_Verify
    @UserId INT,
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.WorkspaceMembers
        WHERE UserId = @UserId AND WorkspaceId = @WorkspaceId
    ) THEN 1 ELSE 0 END AS BIT) AS IsMember;
END
GO
