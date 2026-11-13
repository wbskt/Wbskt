-- Every workspace, deleted ones too (DeletedAt set). Id is also the WorkspaceId the main database's
-- admin views carry.
CREATE VIEW [admin].Workspaces
AS
SELECT
    W.Id,
    W.RefId,
    W.TenantId,
    W.Name,
    W.Description,
    W.OwnerUserId,
    W.CreatedAt,
    DW.DeletedAt,
    (SELECT COUNT(*) FROM dbo.WorkspaceMembers WM WHERE WM.WorkspaceId = W.Id) AS MemberCount
FROM dbo.Workspaces W
LEFT JOIN dbo.DeletedWorkspaces DW ON DW.WorkspaceId = W.Id;
GO
