-- Clears stale presence flags after a socket-host crash (no ClientDisconnectedEvent was
-- published for its connections). Scoped to @HostId so a restarting instance only clears the
-- clients it itself used to hold; survivors on other instances are left untouched. Clients
-- that are actually online reconnect and flip back within their retry window.
CREATE PROCEDURE dbo.Client_ResetAllPresence
    @HostId NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Clients
    SET
        IsConnected = 0,
        ConnectedAt = NULL,
        ConnectedHostId = NULL
    WHERE IsConnected = 1
        AND (@HostId IS NULL OR ConnectedHostId = @HostId);
END
GO
