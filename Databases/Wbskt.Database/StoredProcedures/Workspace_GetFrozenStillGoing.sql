-- The workspaces whose account copy is frozen but which still have a client that can connect or a run
-- in progress: a freeze the TenantFrozenEvent never stopped (the broker was down when it was sent, or
-- the auth host restarted between the commit and the publish). The account sync passes them to
-- dbo.Workspace_Freeze, so the periodic sync repairs the stop as it repairs the copy. Few rows: only
-- frozen workspaces are looked at.
CREATE PROCEDURE dbo.Workspace_GetFrozenStillGoing
AS
BEGIN
    SET NOCOUNT ON;

    SELECT WA.WorkspaceId
    FROM dbo.WorkspaceAccounts WA
    WHERE WA.FrozenAt IS NOT NULL
      AND (EXISTS (SELECT 1 FROM dbo.Clients C WHERE C.WorkspaceId = WA.WorkspaceId AND C.Status IN (0, 1))
           OR EXISTS (SELECT 1 FROM dbo.Runs R WHERE R.WorkspaceId = WA.WorkspaceId AND R.Status IN (N'Running', N'Failing')))
    ORDER BY WA.WorkspaceId;
END
GO
