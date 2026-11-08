-- The workspaces whose usage counts against the same plan limits as @WorkspaceId: every workspace of
-- @TenantId (from dbo.WorkspaceAccounts), and @WorkspaceId itself even before its copy has arrived.
-- With @TenantId NULL (no copy yet, treated as Free) it is @WorkspaceId alone.
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
