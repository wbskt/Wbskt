-- dbo.EventLogs_InsertBatch for client traffic: the event logger splits each batch between the two.
-- The rows it inserts are also counted into dbo.WorkspaceMessageUsage (per day) and
-- dbo.WorkspaceMessageUsageHourly, and per client into dbo.ClientMessageUsageHourly, in the same transaction, so the message meter is exactly the traffic
-- table's count of the same events.
CREATE PROCEDURE dbo.ClientTraffic_InsertBatch
    @Logs dbo.EventLogTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Inserted TABLE (WorkspaceId INT NULL, ClientId INT NULL, EventId INT NOT NULL, CreatedAt DATETIME2(3) NOT NULL);

    BEGIN TRANSACTION;

    -- At-least-once delivery, as for the event log: each MessageId is written once per workspace.
    WITH Incoming AS (
        SELECT
            EventId, EventData, CreatedAtUtc, WorkspaceId, PolicyRefId, ClientRefId, WorkflowRefId, PolicyId, ClientId, WorkflowId, UserId, UserRefId, MessageId, Source, ClientAddress, UserAgent,
            ROW_NUMBER() OVER (PARTITION BY MessageId, WorkspaceId ORDER BY (SELECT NULL)) AS Copy
        FROM @Logs
    )
    INSERT INTO dbo.ClientTraffic (EventId, EventData, CreatedAt, WorkspaceId, PolicyRefId, ClientRefId, WorkflowRefId, PolicyId, ClientId, WorkflowId, UserId, UserRefId, MessageId, Source, ClientAddress, UserAgent)
    OUTPUT inserted.WorkspaceId, inserted.ClientId, inserted.EventId, inserted.CreatedAt INTO @Inserted (WorkspaceId, ClientId, EventId, CreatedAt)
    SELECT i.EventId, i.EventData, i.CreatedAtUtc, i.WorkspaceId, i.PolicyRefId, i.ClientRefId, i.WorkflowRefId, i.PolicyId, i.ClientId, i.WorkflowId, i.UserId, i.UserRefId, i.MessageId, i.Source, i.ClientAddress, i.UserAgent
    FROM Incoming i
    WHERE i.MessageId IS NULL
       OR (i.Copy = 1 AND NOT EXISTS (
              SELECT 1 FROM dbo.ClientTraffic d
              WHERE d.MessageId = i.MessageId
                AND (d.WorkspaceId = i.WorkspaceId OR (d.WorkspaceId IS NULL AND i.WorkspaceId IS NULL))));

    -- Counted per client first; a row without a client (none today) still counts for its workspace.
    DECLARE @ByClient TABLE (WorkspaceId INT NOT NULL, ClientId INT NULL, [Hour] DATETIME2(0) NOT NULL, Inbound BIGINT NOT NULL, Outbound BIGINT NOT NULL);
    INSERT INTO @ByClient (WorkspaceId, ClientId, [Hour], Inbound, Outbound)
    SELECT
        n.WorkspaceId,
        n.ClientId,
        h.[Hour],
        SUM(CASE WHEN e.EventName = N'ClientMessageReceivedEvent' THEN 1 ELSE 0 END),
        SUM(CASE WHEN e.EventName = N'ClientCommandDeliveredEvent' THEN 1 ELSE 0 END)
    FROM @Inserted n
    JOIN dbo.Events e ON e.Id = n.EventId
    CROSS APPLY (SELECT CAST(DATEADD(HOUR, DATEDIFF(HOUR, '19000101', n.CreatedAt), '19000101') AS DATETIME2(0)) AS [Hour]) h
    WHERE n.WorkspaceId IS NOT NULL
      AND e.EventName IN (N'ClientMessageReceivedEvent', N'ClientCommandDeliveredEvent')
    GROUP BY n.WorkspaceId, n.ClientId, h.[Hour];

    DECLARE @Counted TABLE (WorkspaceId INT NOT NULL, [Hour] DATETIME2(0) NOT NULL, Inbound BIGINT NOT NULL, Outbound BIGINT NOT NULL, PRIMARY KEY (WorkspaceId, [Hour]));
    INSERT INTO @Counted (WorkspaceId, [Hour], Inbound, Outbound)
    SELECT WorkspaceId, [Hour], SUM(Inbound), SUM(Outbound)
    FROM @ByClient
    GROUP BY WorkspaceId, [Hour];

    WITH Daily AS (
        SELECT WorkspaceId, CAST([Hour] AS DATE) AS Day, SUM(Inbound) AS Inbound, SUM(Outbound) AS Outbound
        FROM @Counted
        GROUP BY WorkspaceId, CAST([Hour] AS DATE)
    )
    MERGE dbo.WorkspaceMessageUsage WITH (HOLDLOCK) AS t
    USING Daily AS s
       ON t.WorkspaceId = s.WorkspaceId AND t.Day = s.Day
    WHEN MATCHED THEN
        UPDATE SET Inbound = t.Inbound + s.Inbound, Outbound = t.Outbound + s.Outbound, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (WorkspaceId, Day, Inbound, Outbound) VALUES (s.WorkspaceId, s.Day, s.Inbound, s.Outbound);

    MERGE dbo.WorkspaceMessageUsageHourly WITH (HOLDLOCK) AS t
    USING @Counted AS s
       ON t.WorkspaceId = s.WorkspaceId AND t.[Hour] = s.[Hour]
    WHEN MATCHED THEN
        UPDATE SET Inbound = t.Inbound + s.Inbound, Outbound = t.Outbound + s.Outbound
    WHEN NOT MATCHED THEN
        INSERT (WorkspaceId, [Hour], Inbound, Outbound) VALUES (s.WorkspaceId, s.[Hour], s.Inbound, s.Outbound);

    -- A client belongs to one workspace, so (ClientId, Hour) is unique in @ByClient.
    MERGE dbo.ClientMessageUsageHourly WITH (HOLDLOCK) AS t
    USING (SELECT ClientId, [Hour], WorkspaceId, Inbound, Outbound FROM @ByClient WHERE ClientId IS NOT NULL) AS s
       ON t.ClientId = s.ClientId AND t.[Hour] = s.[Hour]
    WHEN MATCHED THEN
        UPDATE SET Inbound = t.Inbound + s.Inbound, Outbound = t.Outbound + s.Outbound
    WHEN NOT MATCHED THEN
        INSERT (ClientId, [Hour], WorkspaceId, Inbound, Outbound) VALUES (s.ClientId, s.[Hour], s.WorkspaceId, s.Inbound, s.Outbound);

    COMMIT TRANSACTION;
END
GO
