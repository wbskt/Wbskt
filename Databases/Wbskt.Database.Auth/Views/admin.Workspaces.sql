-- Every workspace, deleted ones too (DeletedAt set). A live workspace comes from dbo.Workspaces and a
-- deleted one from its tombstone in dbo.DeletedWorkspaces, which has no description or members; a
-- workspace deleted before the tombstone kept its details has only its id and DeletedAt. Id is also
-- the WorkspaceId the main database's admin views carry.
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
    CAST(NULL AS DATETIME2(3)) AS DeletedAt,
    (SELECT COUNT(*) FROM dbo.WorkspaceMembers WM WHERE WM.WorkspaceId = W.Id) AS MemberCount
FROM dbo.Workspaces W

UNION ALL

SELECT
    DW.WorkspaceId,
    DW.RefId,
    DW.TenantId,
    DW.Name,
    CAST(NULL AS NVARCHAR(500)),
    DW.OwnerUserId,
    DW.CreatedAt,
    DW.DeletedAt,
    0
FROM dbo.DeletedWorkspaces DW;
GO
