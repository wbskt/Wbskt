-- Clears the presence of clients held by a socket host that has not sent a heartbeat for
-- @StaleAfterSeconds (see dbo.SocketHosts): it crashed, was replaced by a deploy under a new host
-- id, or was scaled away, so the disconnects it owed will never arrive. Returns the cleared clients
-- so the caller can announce each as disconnected, dated by this sweep. LastActivityAt is left alone:
-- it holds the last change a host saw, and an inferred expiry must not outrank a real connect or
-- disconnect from the outage whose event is still on its way.
--
-- A connection younger than the window is left alone, so a host whose first heartbeat has not been
-- recorded yet is not mistaken for a dead one. The UPDATE claims each row once, so two management
-- instances sweeping together never report the same client twice.
CREATE PROCEDURE dbo.Client_ExpirePresence
    @StaleAfterSeconds INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @StaleBefore DATETIME2(3) = DATEADD(SECOND, -@StaleAfterSeconds, @Now);

    UPDATE c
    SET
        IsConnected = 0,
        ConnectedAt = NULL,
        ConnectedHostId = NULL
    OUTPUT deleted.Id, deleted.RefId, deleted.WorkspaceId, deleted.ConnectedHostId, @Now AS ChangedAt
    FROM dbo.Clients c
    WHERE c.IsConnected = 1
        AND (c.ConnectedAt IS NULL OR c.ConnectedAt < @StaleBefore)
        AND NOT EXISTS (
            SELECT 1
            FROM dbo.SocketHosts h
            WHERE h.HostId = c.ConnectedHostId
                AND h.LastSeenAt >= @StaleBefore);

    -- Hosts gone for a day are history; a host that comes back simply records itself again.
    DELETE FROM dbo.SocketHosts
    WHERE LastSeenAt < DATEADD(DAY, -1, SYSUTCDATETIME());
END
GO
