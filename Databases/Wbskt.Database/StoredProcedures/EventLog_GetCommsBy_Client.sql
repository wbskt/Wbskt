-- Live Comms backfill for the client detail page: the fixed set of in/out protocol events
-- from the generic event log, so the UI has one stable contract regardless of event additions.
-- Newest first, a page at a time from @CursorId, as dbo.EventLog_GetBy_Workspace.
CREATE PROCEDURE dbo.EventLog_GetCommsBy_Client
    @WorkspaceId INT,
    @ClientId INT,
    @Direction NVARCHAR(10) = NULL, -- 'in' | 'out' | NULL for all (lifecycle rows only when all)
    @CursorId BIGINT = NULL,
    @Take INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Names TABLE (EventName NVARCHAR(100) NOT NULL PRIMARY KEY);

    IF @Direction IS NULL OR @Direction = N'in'
    BEGIN
        INSERT INTO @Names (EventName)
        VALUES (N'ClientMessageReceivedEvent'), (N'ClientPongEvent'), (N'ClientCommandAckedEvent');
    END

    IF @Direction IS NULL OR @Direction = N'out'
    BEGIN
        INSERT INTO @Names (EventName)
        VALUES (N'ClientCommandEvent'), (N'ClientCommandDeliveredEvent'), (N'ClientCommandFailedEvent'), (N'ClientPingEvent');
    END

    IF @Direction IS NULL
    BEGIN
        INSERT INTO @Names (EventName)
        VALUES (N'ClientConnectedEvent'), (N'ClientDisconnectedEvent');
    END

    SELECT TOP (@Take)
        el.Id,
        e.EventName,
        el.EventData,
        e.EventCriticality,
        el.PolicyRefId,
        el.ClientRefId,
        el.WorkflowRefId,
        el.CreatedAt
    FROM dbo.EventLogs el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND el.ClientId = @ClientId
      AND (@CursorId IS NULL OR el.Id < @CursorId)
      AND e.EventName IN (SELECT EventName FROM @Names)
    ORDER BY el.Id DESC;
END
GO
