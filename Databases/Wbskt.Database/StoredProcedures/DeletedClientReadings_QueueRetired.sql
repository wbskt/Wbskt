-- Queues the clients of retired workspaces that still have readings, for dbo.ClientReadings_Compact to
-- clear. dbo.Workspace_Retire queues them itself; this catches the workspaces retired before it did,
-- whose account copy was kept with RetiredAt (#739). A workspace deleted before that lost its copy, so
-- nothing here can tell it from a live one; the auth host's list of deleted workspaces is needed for it.
-- Run by the post-deployment script; returns how many clients it queued.
CREATE PROCEDURE dbo.DeletedClientReadings_QueueRetired
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.DeletedClientReadings (ClientId)
    SELECT c.Id
    FROM dbo.Clients c
    JOIN dbo.WorkspaceAccounts a ON a.WorkspaceId = c.WorkspaceId
    WHERE a.RetiredAt IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.DeletedClientReadings d WHERE d.ClientId = c.Id)
      AND (EXISTS (SELECT 1 FROM dbo.ClientReadings r WHERE r.ClientId = c.Id)
           OR EXISTS (SELECT 1 FROM dbo.ClientReadingsHourly h WHERE h.ClientId = c.Id));

    SELECT @@ROWCOUNT;
END
GO
