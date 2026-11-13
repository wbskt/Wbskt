-- What each workspace holds. Deleted workflows (dbo.WorkflowDeletions) and test definitions are left out.
CREATE VIEW [admin].WorkspaceCounts
AS
SELECT
    WA.WorkspaceId,
    (SELECT COUNT(*) FROM dbo.Clients C WHERE C.WorkspaceId = WA.WorkspaceId) AS ClientCount,
    (SELECT COUNT(*) FROM dbo.Clients C WHERE C.WorkspaceId = WA.WorkspaceId AND C.IsConnected = 1) AS ConnectedClientCount,
    (SELECT COUNT(*) FROM dbo.RegistrationPolicies RP WHERE RP.WorkspaceId = WA.WorkspaceId) AS PolicyCount,
    (SELECT COUNT(DISTINCT WD.RefId)
     FROM dbo.WorkflowDefinitions WD
     WHERE WD.WorkspaceId = WA.WorkspaceId
       AND WD.Kind = N'Version'
       AND NOT EXISTS (SELECT 1 FROM dbo.WorkflowDeletions D WHERE D.RefId = WD.RefId)) AS WorkflowCount
FROM dbo.WorkspaceAccounts WA;
GO
