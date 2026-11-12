-- The workspaces whose usage counts against the same plan limits as @WorkspaceId: every workspace of
-- @TenantId (from dbo.WorkspaceAccounts), and @WorkspaceId itself even before its copy has arrived.
-- With @TenantId NULL (no copy yet, treated as Free) it is @WorkspaceId alone. A deleted workspace's copy
-- is kept (RetiredAt), so what it spent still counts; it has nothing running and no live clients, so it
-- adds nothing to the counts of those.
CREATE FUNCTION dbo.TenantWorkspaces (@WorkspaceId INT, @TenantId INT)
RETURNS TABLE
AS
RETURN
(
    SELECT @WorkspaceId AS WorkspaceId
    UNION
    SELECT WorkspaceId
    FROM dbo.WorkspaceAccounts
    WHERE TenantId = @TenantId
);
GO
