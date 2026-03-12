CREATE PROCEDURE dbo.EventLogs_InsertBatch
    @Logs dbo.EventLogTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.EventLogs (EventId, EventData, CreatedAt, WorkspaceId)
    SELECT EventId, EventData, CreatedAtUtc, WorkspaceId
    FROM @Logs;
END
GO
