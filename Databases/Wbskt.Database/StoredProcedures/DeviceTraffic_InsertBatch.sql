-- dbo.EventLogs_InsertBatch for device traffic: the event logger splits each batch between the two.
CREATE PROCEDURE dbo.DeviceTraffic_InsertBatch
    @Logs dbo.EventLogTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- At-least-once delivery, as for the event log: each MessageId is written once per workspace.
    WITH Incoming AS (
        SELECT *, ROW_NUMBER() OVER (PARTITION BY MessageId, WorkspaceId ORDER BY (SELECT NULL)) AS Copy
        FROM @Logs
    )
    INSERT INTO dbo.DeviceTraffic (EventId, EventData, CreatedAt, WorkspaceId, PolicyRefId, ClientRefId, WorkflowRefId, PolicyId, ClientId, WorkflowId, UserId, UserRefId, MessageId, Source, ClientAddress, UserAgent)
    SELECT i.EventId, i.EventData, i.CreatedAtUtc, i.WorkspaceId, i.PolicyRefId, i.ClientRefId, i.WorkflowRefId, i.PolicyId, i.ClientId, i.WorkflowId, i.UserId, i.UserRefId, i.MessageId, i.Source, i.ClientAddress, i.UserAgent
    FROM Incoming i
    WHERE i.MessageId IS NULL
       OR (i.Copy = 1 AND NOT EXISTS (
              SELECT 1 FROM dbo.DeviceTraffic d
              WHERE d.MessageId = i.MessageId
                AND (d.WorkspaceId = i.WorkspaceId OR (d.WorkspaceId IS NULL AND i.WorkspaceId IS NULL))));
END
GO
