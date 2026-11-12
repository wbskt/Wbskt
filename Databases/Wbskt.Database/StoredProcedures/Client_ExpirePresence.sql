-- Clears the presence of clients held by a socket host that has not sent a heartbeat for
-- @StaleAfterSeconds (see dbo.SocketHosts): it crashed, was replaced by a deploy under a new host
-- id, or was scaled away, so the disconnects it owed will never arrive. Returns the cleared clients
-- so the caller can announce each as disconnected, dated by the expiry recorded here (never earlier
-- than the row's last change), so a snapshot that sets the client back online can be dated after it.
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
        ConnectedHostId = NULL,
        LastActivityAt = CASE WHEN c.LastActivityAt > @Now THEN c.LastActivityAt ELSE @Now END
    OUTPUT deleted.Id, deleted.RefId, deleted.WorkspaceId, deleted.ConnectedHostId, inserted.LastActivityAt AS ChangedAt
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
