-- Adds a batch of received messages, already summed per client and type, to dbo.ClientMessageTypes.
-- A new type is stored only while the client has fewer than @MaxPerClient, so a device cannot grow
-- the table without bound by varying types; the newest new types are kept first. A client deleted
-- meanwhile is skipped rather than failing the batch.
-- One transaction: when two consumers race to insert the same new type, the loser rolls back whole and
-- its batch is retried, rather than keeping counts it already added and adding them again on the retry.
CREATE PROCEDURE dbo.ClientMessageTypes_Record
    @Rows dbo.ClientMessageTypeTableType READONLY,
    @MaxPerClient INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE t
    SET
        MessageCount = t.MessageCount + r.MessageCount,
        LastSeenAt = IIF(r.LastSeenAt > t.LastSeenAt, r.LastSeenAt, t.LastSeenAt),
        SamplePayload = IIF(r.LastSeenAt >= t.LastSeenAt AND r.SamplePayload IS NOT NULL, r.SamplePayload, t.SamplePayload)
    FROM dbo.ClientMessageTypes t
    INNER JOIN @Rows r ON r.ClientId = t.ClientId AND r.Type = t.Type;

    INSERT INTO dbo.ClientMessageTypes (ClientId, Type, MessageCount, FirstSeenAt, LastSeenAt, SamplePayload)
    SELECT n.ClientId, n.Type, n.MessageCount, n.LastSeenAt, n.LastSeenAt, n.SamplePayload
    FROM (
        SELECT r.*, ROW_NUMBER() OVER (PARTITION BY r.ClientId ORDER BY r.LastSeenAt DESC) AS Position
        FROM @Rows r
        WHERE NOT EXISTS (SELECT 1 FROM dbo.ClientMessageTypes t WHERE t.ClientId = r.ClientId AND t.Type = r.Type)
          AND EXISTS (SELECT 1 FROM dbo.Clients c WHERE c.Id = r.ClientId)
    ) n
    WHERE n.Position + (SELECT COUNT(*) FROM dbo.ClientMessageTypes t WHERE t.ClientId = n.ClientId) <= @MaxPerClient;

    COMMIT TRANSACTION;
END
GO
