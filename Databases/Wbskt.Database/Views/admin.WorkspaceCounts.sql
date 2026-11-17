-- What each workspace holds. Deleted workflows and test definitions are left out.
CREATE VIEW [admin].WorkspaceCounts
AS
SELECT
    WA.WorkspaceId,
    (SELECT COUNT(*) FROM dbo.Clients C WHERE C.WorkspaceId = WA.WorkspaceId) AS ClientCount,
    (SELECT COUNT(*) FROM dbo.Clients C WHERE C.WorkspaceId = WA.WorkspaceId AND C.IsConnected = 1) AS ConnectedClientCount,
    (SELECT COUNT(*) FROM dbo.RegistrationPolicies RP WHERE RP.WorkspaceId = WA.WorkspaceId) AS PolicyCount,
    (SELECT COUNT(*)
     FROM dbo.Workflows W
     WHERE W.WorkspaceId = WA.WorkspaceId
       AND W.State IN (1, 2, 3)) AS WorkflowCount
FROM dbo.WorkspaceAccounts WA;
GO
