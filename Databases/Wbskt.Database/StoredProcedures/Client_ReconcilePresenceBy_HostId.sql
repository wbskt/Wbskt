-- Repairs the presence of a live socket host's clients from the host's own snapshot
-- (@ConnectedRefIds, taken at @TakenAt on the host's clock), both ways:
--
-- * A client the database has on @HostId that the snapshot says the host no longer holds is cleared:
--   its disconnect was lost on a host that is still alive, which neither the heartbeat lease nor the
--   start-up reset catches.
-- * A client in the snapshot that the database shows offline is set back online on @HostId: the
--   stale-presence sweep cleared it while the host's heartbeats could not get through (the broker down
--   or partitioned), though the host kept the connection. Only a Registered client: a revoke the host
--   missed in the same outage is closed by the host's own access re-check, not shown online here.
--
-- Only a presence change older than @TakenAt by @MarginSeconds is repaired, so a connect or disconnect
-- made while the snapshot was being taken, whose event is still on its way, is left to that event.
-- Returns each repaired client, which way and the time to date its announcement with. Both run in one
-- transaction, so a retry after a failure repeats both and no clear goes unannounced.
CREATE PROCEDURE dbo.Client_ReconcilePresenceBy_HostId
    @HostId NVARCHAR(100),
    @TakenAt DATETIME2(3),
    @MarginSeconds INT,
    @ConnectedRefIds dbo.RefIdTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ChangedBefore DATETIME2(3) = DATEADD(SECOND, -@MarginSeconds, @TakenAt);
    DECLARE @Repaired TABLE
    (
        Id INT NOT NULL,
        RefId UNIQUEIDENTIFIER NOT NULL,
        WorkspaceId INT NOT NULL,
        Connected BIT NOT NULL,
        ChangedAt DATETIME2(3) NOT NULL
    );

    BEGIN TRANSACTION;

    UPDATE c
    SET
        IsConnected = 0,
        ConnectedAt = NULL,
        ConnectedHostId = NULL
    OUTPUT deleted.Id, deleted.RefId, deleted.WorkspaceId, 0, @TakenAt INTO @Repaired (Id, RefId, WorkspaceId, Connected, ChangedAt)
    FROM dbo.Clients c
    WHERE c.IsConnected = 1
        AND c.ConnectedHostId = @HostId
        AND c.ConnectedAt < @ChangedBefore
        AND NOT EXISTS (SELECT 1 FROM @ConnectedRefIds r WHERE r.RefId = c.RefId);

    -- Dated just after the last change a host saw (the sweep's expiry does not write LastActivityAt), not
    -- at the snapshot: a connect or disconnect from the outage, still on its way, wins over the restore.
    -- @TakenAt is on the host's clock; the margin assumes it is within a minute of the others.
    UPDATE c
    SET
        IsConnected = 1,
        ConnectedAt = DATEADD(MILLISECOND, 1, ISNULL(c.LastActivityAt, @ChangedBefore)),
        ConnectedHostId = @HostId,
        LastActivityAt = DATEADD(MILLISECOND, 1, ISNULL(c.LastActivityAt, @ChangedBefore)),
        UpdatingUntil = NULL
    OUTPUT inserted.Id, inserted.RefId, inserted.WorkspaceId, 1, inserted.LastActivityAt INTO @Repaired (Id, RefId, WorkspaceId, Connected, ChangedAt)
    FROM dbo.Clients c
    JOIN @ConnectedRefIds r ON r.RefId = c.RefId
    WHERE c.IsConnected = 0
        AND c.Status = 1
        AND (c.LastActivityAt IS NULL OR c.LastActivityAt < @ChangedBefore);

    COMMIT TRANSACTION;

    SELECT
        Id,
        RefId,
        WorkspaceId,
        Connected,
        ChangedAt
    FROM @Repaired;
END
GO
