CREATE PROCEDURE dbo.WorkspaceMember_GetAll
    @WorkspaceId INT,
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.WorkspaceMembers WM
    INNER JOIN dbo.Users U ON U.Id = WM.UserId
    WHERE WM.WorkspaceId = @WorkspaceId;

    SELECT
        U.RefId,
        U.Username,
        U.Email,
        U.IsActive,
        CAST(ISNULL(TM.IsSuspended, 0) AS BIT) AS IsSuspended
    FROM dbo.WorkspaceMembers WM
    INNER JOIN dbo.Users U ON U.Id = WM.UserId
    INNER JOIN dbo.Workspaces W ON W.Id = WM.WorkspaceId
    LEFT JOIN dbo.TenantMembers TM ON TM.TenantId = W.TenantId AND TM.UserId = WM.UserId
    WHERE WM.WorkspaceId = @WorkspaceId
    ORDER BY U.Username
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO
