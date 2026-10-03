-- Deletes up to @BatchSize runs that completed before @CutoffUtc, with everything that hangs off
-- them, and returns how many runs went. HistoryRetentionGc calls it with the elevated window - the
-- point at which even a run's Warn/Error history has been deleted - so a run is only removed once
-- there is nothing left to inspect but its summary row.
CREATE PROCEDURE dbo.Run_DeleteRetired
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Runs TABLE (Id INT NOT NULL PRIMARY KEY);

    INSERT INTO @Runs (Id)
    SELECT TOP (@BatchSize) Id
    FROM dbo.Runs
    WHERE CompletedAt IS NOT NULL
      AND CompletedAt < @CutoffUtc
    ORDER BY CompletedAt;

    BEGIN TRANSACTION;

    DELETE FROM dbo.HistoryEvents  WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.Bookmarks      WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.IdempotencyKeys WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE C
    FROM dbo.JoinContributions C
    INNER JOIN dbo.JoinAggregators A ON A.JoinToken = C.JoinToken
    WHERE A.RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.JoinAggregators WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.RunCounters    WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.Branches       WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.Runs           WHERE Id IN (SELECT Id FROM @Runs);

    COMMIT TRANSACTION;

    SELECT COUNT(*) FROM @Runs;
END;
GO
