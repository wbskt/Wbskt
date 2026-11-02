-- Clears the presence of clients the database has on @HostId that the host's own snapshot
-- (@ConnectedRefIds, taken at @TakenAt on the host's clock) says it no longer holds: their
-- disconnect was lost on a host that is still alive, which neither the heartbeat lease nor the
-- start-up reset catches. Only a connection older than @TakenAt by @MarginSeconds is cleared, so
-- one that connected while the snapshot was being taken stays. Returns the cleared clients so the
-- caller can announce each as disconnected; the UPDATE claims each row once.
CREATE PROCEDURE dbo.Client_ReconcilePresenceBy_HostId
    @HostId NVARCHAR(100),
    @TakenAt DATETIME2(3),
    @MarginSeconds INT,
    @ConnectedRefIds dbo.RefIdTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ConnectedBefore DATETIME2(3) = DATEADD(SECOND, -@MarginSeconds, @TakenAt);

    UPDATE c
    SET
        IsConnected = 0,
        ConnectedAt = NULL,
        ConnectedHostId = NULL
    OUTPUT deleted.Id, deleted.RefId, deleted.WorkspaceId, deleted.ConnectedHostId
    FROM dbo.Clients c
    WHERE c.IsConnected = 1
        AND c.ConnectedHostId = @HostId
        AND c.ConnectedAt < @ConnectedBefore
        AND NOT EXISTS (SELECT 1 FROM @ConnectedRefIds r WHERE r.RefId = c.RefId);
END
GO
