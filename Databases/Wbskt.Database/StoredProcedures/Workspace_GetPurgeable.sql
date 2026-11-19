-- Deleted workspaces (a retired account copy) where dbo.Workspace_Purge has something to remove now,
-- tested the way it tests each stage. One still waiting on its readings or runs is left out until they
-- go, so a backlog of those cannot fill @BatchSize and hold back workspaces that are ready.
CREATE PROCEDURE dbo.Workspace_GetPurgeable
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@BatchSize)
        A.WorkspaceId
    FROM dbo.WorkspaceAccounts A
    WHERE A.RetiredAt IS NOT NULL
      AND (
            -- A client whose readings are cleared.
            EXISTS (SELECT 1
                    FROM dbo.Clients C
                    WHERE C.WorkspaceId = A.WorkspaceId
                      AND C.Status IN (2, 3)
                      AND NOT EXISTS (SELECT 1 FROM dbo.ClientReadings R WHERE R.ClientId = C.Id)
                      AND NOT EXISTS (SELECT 1 FROM dbo.ClientReadingsHourly H WHERE H.ClientId = C.Id))
            -- Policies or templates, once no client is left.
            OR (NOT EXISTS (SELECT 1 FROM dbo.Clients C WHERE C.WorkspaceId = A.WorkspaceId)
                AND (EXISTS (SELECT 1 FROM dbo.RegistrationPolicies P WHERE P.WorkspaceId = A.WorkspaceId)
                     OR EXISTS (SELECT 1 FROM dbo.MessageTemplates T WHERE T.WorkspaceId = A.WorkspaceId)))
            -- Workflows or definitions, once no run of them is left.
            OR (NOT EXISTS (SELECT 1 FROM dbo.Runs R WHERE R.WorkspaceId = A.WorkspaceId)
                AND (EXISTS (SELECT 1 FROM dbo.Workflows W WHERE W.WorkspaceId = A.WorkspaceId)
                     OR EXISTS (SELECT 1 FROM dbo.WorkflowDefinitions D WHERE D.WorkspaceId = A.WorkspaceId)))
          )
    ORDER BY A.RetiredAt, A.WorkspaceId;
END
GO
