-- dbo.EventLogs_InsertBatch for client traffic: the event logger splits each batch between the two.
-- The rows it inserts are also counted into dbo.WorkspaceMessageUsage, in the same transaction, so the
-- message meter is exactly the traffic table's count of the same events.
CREATE PROCEDURE dbo.ClientTraffic_InsertBatch
    @Logs dbo.EventLogTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Inserted TABLE (WorkspaceId INT NULL, EventId INT NOT NULL, CreatedAt DATETIME2(3) NOT NULL);

    BEGIN TRANSACTION;

    -- At-least-once delivery, as for the event log: each MessageId is written once per workspace.
    WITH Incoming AS (
        SELECT *, ROW_NUMBER() OVER (PARTITION BY MessageId, WorkspaceId ORDER BY (SELECT NULL)) AS Copy
        FROM @Logs
    )
    INSERT INTO dbo.ClientTraffic (EventId, EventData, CreatedAt, WorkspaceId, PolicyRefId, ClientRefId, WorkflowRefId, PolicyId, ClientId, WorkflowId, UserId, UserRefId, MessageId, Source, ClientAddress, UserAgent)
    OUTPUT inserted.WorkspaceId, inserted.EventId, inserted.CreatedAt INTO @Inserted (WorkspaceId, EventId, CreatedAt)
    SELECT i.EventId, i.EventData, i.CreatedAtUtc, i.WorkspaceId, i.PolicyRefId, i.ClientRefId, i.WorkflowRefId, i.PolicyId, i.ClientId, i.WorkflowId, i.UserId, i.UserRefId, i.MessageId, i.Source, i.ClientAddress, i.UserAgent
    FROM Incoming i
    WHERE i.MessageId IS NULL
       OR (i.Copy = 1 AND NOT EXISTS (
              SELECT 1 FROM dbo.ClientTraffic d
              WHERE d.MessageId = i.MessageId
                AND (d.WorkspaceId = i.WorkspaceId OR (d.WorkspaceId IS NULL AND i.WorkspaceId IS NULL))));

    WITH Counted AS (
        SELECT
            n.WorkspaceId,
            CAST(n.CreatedAt AS DATE) AS Day,
            SUM(CASE WHEN e.EventName = N'ClientMessageReceivedEvent' THEN 1 ELSE 0 END) AS Inbound,
            SUM(CASE WHEN e.EventName = N'ClientCommandDeliveredEvent' THEN 1 ELSE 0 END) AS Outbound
        FROM @Inserted n
        JOIN dbo.Events e ON e.Id = n.EventId
        WHERE n.WorkspaceId IS NOT NULL
          AND e.EventName IN (N'ClientMessageReceivedEvent', N'ClientCommandDeliveredEvent')
        GROUP BY n.WorkspaceId, CAST(n.CreatedAt AS DATE)
    )
    MERGE dbo.WorkspaceMessageUsage WITH (HOLDLOCK) AS t
    USING Counted AS s
       ON t.WorkspaceId = s.WorkspaceId AND t.Day = s.Day
    WHEN MATCHED THEN
        UPDATE SET Inbound = t.Inbound + s.Inbound, Outbound = t.Outbound + s.Outbound, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (WorkspaceId, Day, Inbound, Outbound) VALUES (s.WorkspaceId, s.Day, s.Inbound, s.Outbound);

    COMMIT TRANSACTION;
END
GO
