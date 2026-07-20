-- Clears stale presence flags after a socket-host crash (no ClientDisconnectedEvent was
-- published for its connections). Valid while the socket tier is single-node; clients that
-- are actually online reconnect and flip back within their retry window.
CREATE PROCEDURE dbo.Client_ResetAllPresence
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Clients
    SET
        IsConnected = 0,
        ConnectedAt = NULL
    WHERE IsConnected = 1;
END
GO
