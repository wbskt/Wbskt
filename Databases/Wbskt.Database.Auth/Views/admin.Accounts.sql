-- Every account (tenant) with its plan. Id is also the TenantId the main database's admin views carry.
CREATE VIEW [admin].Accounts
AS
SELECT
    T.Id,
    T.RefId,
    T.Name,
    T.Description,
    T.PlanCode,
    T.PlanSource,
    T.PlanRevision,
    T.PlanChangedAt,
    T.PlanEndsAt,
    T.PlanLimitOverrides,
    T.CreatedAt,
    (SELECT COUNT(*) FROM dbo.TenantMembers TM WHERE TM.TenantId = T.Id) AS MemberCount,
    (SELECT COUNT(*)
     FROM dbo.Workspaces W
     WHERE W.TenantId = T.Id
       AND NOT EXISTS (SELECT 1 FROM dbo.DeletedWorkspaces DW WHERE DW.WorkspaceId = W.Id)) AS WorkspaceCount
FROM dbo.Tenants T;
GO
