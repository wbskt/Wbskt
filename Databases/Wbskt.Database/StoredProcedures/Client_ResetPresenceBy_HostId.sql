-- Clears the presence of every client a socket host held before it restarted under the same host
-- id (a container restart keeps its hostname). Those connections died with the process and never
-- got a ClientDisconnectedEvent; the heartbeat lease cannot catch them either, because the restarted
-- host renews that lease at once. Scoped to @HostId so survivors on other instances are untouched.
-- Returns the cleared clients so the caller can announce each as disconnected; clients that are
-- actually online reconnect within their retry window and flip back through Client_UpdatePresence.
-- @StartedAt is when the host started, by its own clock: a client that already reconnected to it
-- (ConnectedAt at or after the start) is left alone, however late this repair runs. NULL clears all,
-- for a caller from before the parameter existed.
CREATE PROCEDURE dbo.Client_ResetPresenceBy_HostId
    @HostId NVARCHAR(100),
    @StartedAt DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Clients
    SET
        IsConnected = 0,
        ConnectedAt = NULL,
        ConnectedHostId = NULL
    OUTPUT deleted.Id, deleted.RefId, deleted.WorkspaceId, deleted.ConnectedHostId
    WHERE IsConnected = 1
        AND ConnectedHostId = @HostId
        AND (@StartedAt IS NULL OR ConnectedAt < @StartedAt);
END
GO
