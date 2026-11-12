-- Repairs the presence of a live socket host's clients from the host's own snapshot
-- (@ConnectedRefIds, taken at @TakenAt on the host's clock), both ways:
--
-- * A client the database has on @HostId that the snapshot says the host no longer holds is cleared:
--   its disconnect was lost on a host that is still alive, which neither the heartbeat lease nor the
--   start-up reset catches.
-- * A client in the snapshot that the database shows offline is set back online on @HostId: the
--   stale-presence sweep cleared it while the host's heartbeats could not get through (the broker down
--   or partitioned), though the host kept the connection.
--
-- Only a presence change older than @TakenAt by @MarginSeconds is repaired, so a connect or disconnect
-- made while the snapshot was being taken, whose event is still on its way, is left to that event.
-- Returns each repaired client and which way, so the caller can announce it; each UPDATE claims a row
-- once.
CREATE PROCEDURE dbo.Client_ReconcilePresenceBy_HostId
    @HostId NVARCHAR(100),
    @TakenAt DATETIME2(3),
    @MarginSeconds INT,
    @ConnectedRefIds dbo.RefIdTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangedBefore DATETIME2(3) = DATEADD(SECOND, -@MarginSeconds, @TakenAt);
    DECLARE @Repaired TABLE
    (
        Id INT NOT NULL,
        RefId UNIQUEIDENTIFIER NOT NULL,
        WorkspaceId INT NOT NULL,
        Connected BIT NOT NULL
    );

    UPDATE c
    SET
        IsConnected = 0,
        ConnectedAt = NULL,
        ConnectedHostId = NULL
    OUTPUT deleted.Id, deleted.RefId, deleted.WorkspaceId, 0 INTO @Repaired (Id, RefId, WorkspaceId, Connected)
    FROM dbo.Clients c
    WHERE c.IsConnected = 1
        AND c.ConnectedHostId = @HostId
        AND c.ConnectedAt < @ChangedBefore
        AND NOT EXISTS (SELECT 1 FROM @ConnectedRefIds r WHERE r.RefId = c.RefId);

    -- Dated by the snapshot, as Client_UpdatePresence dates a connect, so a disconnect the host saw
    -- after it still wins.
    UPDATE c
    SET
        IsConnected = 1,
        ConnectedAt = @TakenAt,
        ConnectedHostId = @HostId,
        LastActivityAt = @TakenAt,
        UpdatingUntil = NULL
    OUTPUT inserted.Id, inserted.RefId, inserted.WorkspaceId, 1 INTO @Repaired (Id, RefId, WorkspaceId, Connected)
    FROM dbo.Clients c
    JOIN @ConnectedRefIds r ON r.RefId = c.RefId
    WHERE c.IsConnected = 0
        AND (c.LastActivityAt IS NULL OR c.LastActivityAt < @ChangedBefore);

    SELECT
        Id,
        RefId,
        WorkspaceId,
        Connected
    FROM @Repaired;
END
GO
