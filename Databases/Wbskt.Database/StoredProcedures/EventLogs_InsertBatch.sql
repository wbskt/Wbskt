CREATE PROCEDURE dbo.EventLogs_InsertBatch
    @Logs dbo.EventLogTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.EventLogs (EventId, EventData, CreatedAt, WorkspaceId, PolicyRefId, ClientRefId, WorkflowRefId, PolicyId, ClientId, WorkflowId, UserId, UserRefId)
    SELECT EventId, EventData, CreatedAtUtc, WorkspaceId, PolicyRefId, ClientRefId, WorkflowRefId, PolicyId, ClientId, WorkflowId, UserId, UserRefId
    FROM @Logs;
END
GO
