CREATE PROCEDURE dbo.EventLogs_InsertBatch
    @Logs dbo.EventLogTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- At-least-once delivery: a batch can be redelivered after it was saved, and a batch can carry
    -- the same message twice. Each MessageId is written once per workspace (a message about a
    -- tenant's people is logged in each of its workspaces); rows without one are always written.
    WITH Incoming AS (
        SELECT *, ROW_NUMBER() OVER (PARTITION BY MessageId, WorkspaceId ORDER BY (SELECT NULL)) AS Copy
        FROM @Logs
    )
    INSERT INTO dbo.EventLogs (EventId, EventData, CreatedAt, WorkspaceId, PolicyRefId, ClientRefId, WorkflowRefId, PolicyId, ClientId, WorkflowId, UserId, UserRefId, MessageId, Source, ClientAddress, UserAgent)
    SELECT i.EventId, i.EventData, i.CreatedAtUtc, i.WorkspaceId, i.PolicyRefId, i.ClientRefId, i.WorkflowRefId, i.PolicyId, i.ClientId, i.WorkflowId, i.UserId, i.UserRefId, i.MessageId, i.Source, i.ClientAddress, i.UserAgent
    FROM Incoming i
    WHERE i.MessageId IS NULL
       OR (i.Copy = 1 AND NOT EXISTS (
              SELECT 1 FROM dbo.EventLogs e
              WHERE e.MessageId = i.MessageId
                AND (e.WorkspaceId = i.WorkspaceId OR (e.WorkspaceId IS NULL AND i.WorkspaceId IS NULL))));
END
GO
