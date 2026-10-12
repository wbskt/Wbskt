-- The steps that use an integration in each live workflow's current version: the newest version
-- of a workflow that has not been deleted. Status follows that version's IsEnabled, so a workflow
-- whose current version is deprecated reads as a deprecated use (which does not block a delete).
CREATE VIEW dbo.IntegrationUsages
AS
SELECT
    R.IntegrationId,
    R.NodeId,
    R.NodeName,
    WD.Id        AS WorkflowDefinitionId,
    WD.RefId     AS WorkflowRefId,
    WD.Name      AS WorkflowName,
    WD.Version,
    WD.IsEnabled
FROM dbo.WorkflowIntegrationRefs R
INNER JOIN dbo.WorkflowDefinitions WD ON WD.Id = R.WorkflowDefinitionId
WHERE WD.Version = (SELECT MAX(V.Version) FROM dbo.WorkflowDefinitions V WHERE V.RefId = WD.RefId)
  AND NOT EXISTS (SELECT 1 FROM dbo.WorkflowDeletions D WHERE D.RefId = WD.RefId);
GO
