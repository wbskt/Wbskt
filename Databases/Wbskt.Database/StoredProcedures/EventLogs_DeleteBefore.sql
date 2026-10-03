-- Retention sweep for the event log: deletes up to @BatchSize rows older than @CutoffUtc and returns
-- how many went. The caller repeats until a batch comes back short, so no single statement holds
-- locks across a large delete. Safe to run from several management hosts at once: READPAST lets
-- each skip rows another is already deleting.
CREATE PROCEDURE dbo.EventLogs_DeleteBefore
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE TOP (@BatchSize)
    FROM dbo.EventLogs WITH (READPAST)
    WHERE CreatedAt < @CutoffUtc;

    SELECT @@ROWCOUNT AS Deleted;
END
GO
