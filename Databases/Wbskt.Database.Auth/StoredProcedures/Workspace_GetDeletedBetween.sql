-- The workspaces deleted in [@Since, @Until), for the management host's deleted workspace sweep
-- (GET /api/internal/workspaces/deleted). The sweep asks for a window several times longer than its
-- interval and relies on dbo.Workspace_Retire being safe to repeat, so no watermark is kept.
CREATE PROCEDURE dbo.Workspace_GetDeletedBetween
    @Since DATETIME2(3),
    @Until DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT WorkspaceId, DeletedAt
    FROM dbo.DeletedWorkspaces
    WHERE DeletedAt >= @Since
      AND DeletedAt < @Until
    ORDER BY WorkspaceId;
END
GO
