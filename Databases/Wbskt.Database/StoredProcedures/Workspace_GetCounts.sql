-- What a workspace holds, for its card on the workspaces list and the Overview: clients by state,
-- policies, and workflows by state. Deleted workflows are left out.
CREATE PROCEDURE dbo.Workspace_GetCounts
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ISNULL(C.Online, 0) AS ClientsOnline,
        ISNULL(C.Offline, 0) AS ClientsOffline,
        ISNULL(C.Pending, 0) AS ClientsPending,
        ISNULL(C.Revoked, 0) AS ClientsRevoked,
        ISNULL(C.Rejected, 0) AS ClientsRejected,
        (SELECT COUNT(*) FROM dbo.RegistrationPolicies RP WHERE RP.WorkspaceId = @WorkspaceId) AS Policies,
        ISNULL(W.Enabled, 0) AS WorkflowsEnabled,
        ISNULL(W.Disabled, 0) AS WorkflowsDisabled,
        ISNULL(W.Deprecated, 0) AS WorkflowsDeprecated
    FROM (SELECT 1 AS One) AS D
    OUTER APPLY (
        SELECT
            SUM(CASE WHEN Status = 1 AND IsConnected = 1 THEN 1 ELSE 0 END) AS Online,
            SUM(CASE WHEN Status = 1 AND IsConnected = 0 THEN 1 ELSE 0 END) AS Offline,
            SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END) AS Pending,
            SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) AS Revoked,
            SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END) AS Rejected
        FROM dbo.Clients
        WHERE WorkspaceId = @WorkspaceId) AS C
    OUTER APPLY (
        SELECT
            SUM(CASE WHEN State = 1 THEN 1 ELSE 0 END) AS Enabled,
            SUM(CASE WHEN State = 2 THEN 1 ELSE 0 END) AS Disabled,
            SUM(CASE WHEN State = 3 THEN 1 ELSE 0 END) AS Deprecated
        FROM dbo.Workflows
        WHERE WorkspaceId = @WorkspaceId
          AND State IN (1, 2, 3)) AS W;
END
GO
