-- The steps that use an integration in each workflow that still exists: in the live version when
-- the workflow is Enabled (IsLive = 1), and otherwise in its newest version, the one an author
-- would enable next (IsLive = 0). Only a live use blocks deleting the integration; enabling a
-- version that names a deleted integration is refused instead.
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
    CAST(CASE WHEN W.State = 1 THEN 1 ELSE 0 END AS BIT) AS IsLive
FROM dbo.WorkflowIntegrationRefs R
INNER JOIN dbo.WorkflowDefinitions WD ON WD.Id = R.WorkflowDefinitionId
INNER JOIN dbo.Workflows W ON W.RefId = WD.RefId AND W.State IN (1, 2, 3)
WHERE WD.Kind = N'Version'
  AND ((W.State = 1 AND WD.Id = W.LiveDefinitionId)
       OR (W.State <> 1 AND WD.Version = (SELECT MAX(V.Version) FROM dbo.WorkflowDefinitions V WHERE V.RefId = WD.RefId AND V.Kind = N'Version')));
GO
