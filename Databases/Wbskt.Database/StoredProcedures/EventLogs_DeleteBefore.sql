-- Retention sweep for the event log: deletes up to @BatchSize rows older than @CutoffUtc and returns
-- how many went. The caller repeats until a batch comes back short, so no single statement holds
-- locks across a large delete. Safe to run from several management hosts at once: READPAST lets
-- each skip rows another is already deleting.
--
-- @EventIds (comma-separated dbo.Events ids) limits the sweep to those events. The device traffic
-- pass uses it to apply its shorter retention; NULL sweeps every event.
CREATE PROCEDURE dbo.EventLogs_DeleteBefore
    @CutoffUtc DATETIME2(3),
    @BatchSize INT,
    @EventIds  NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @EventIds IS NULL
    BEGIN
        DELETE TOP (@BatchSize)
        FROM dbo.EventLogs WITH (READPAST)
        WHERE CreatedAt < @CutoffUtc;
    END
    ELSE
    BEGIN
        DELETE TOP (@BatchSize)
        FROM dbo.EventLogs WITH (READPAST)
        WHERE CreatedAt < @CutoffUtc
          AND EventId IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@EventIds, N','));
    END

    SELECT @@ROWCOUNT AS Deleted;
END
GO
