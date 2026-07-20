-- Live Comms backfill for the client detail page: the fixed set of in/out protocol events
-- from the generic event log, so the UI has one stable contract regardless of event additions.
CREATE PROCEDURE dbo.EventLog_GetCommsBy_Client
    @WorkspaceId INT,
    @ClientId INT,
    @Direction NVARCHAR(10) = NULL, -- 'in' | 'out' | NULL for all (lifecycle rows only when all)
    @Skip INT = 0,
    @Take INT = 50,
    @TotalCount INT OUTPUT
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

    SELECT @TotalCount = COUNT(*)
    FROM dbo.EventLogs el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND el.ClientId = @ClientId
      AND e.EventName IN (SELECT EventName FROM @Names);

    SELECT
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
      AND e.EventName IN (SELECT EventName FROM @Names)
    ORDER BY el.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO
