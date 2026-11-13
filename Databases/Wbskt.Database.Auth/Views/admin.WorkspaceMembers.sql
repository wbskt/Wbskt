CREATE VIEW [admin].WorkspaceMembers
AS
SELECT
    WM.WorkspaceId,
    WM.UserId
FROM dbo.WorkspaceMembers WM;
GO
