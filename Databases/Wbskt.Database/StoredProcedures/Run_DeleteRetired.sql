-- Deletes up to @BatchSize runs that completed before @CutoffUtc, with everything that hangs off
-- them, and the test copies of workflows that no run points at any more. Returns how many runs went.
-- HistoryRetentionGc calls it with the elevated window - the point at which even a run's Warn/Error
-- history has been deleted - so a run is only removed once there is nothing left to inspect but its
-- summary row.
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

    -- Credits a run consumed after its last post go to the ledger before its counter is deleted, so the
    -- ledger still adds up to what every run consumed. Same lock order as RunCredits_Post.
    DECLARE @Moved TABLE (RunId INT NOT NULL PRIMARY KEY, Credits DECIMAL(18,4) NOT NULL);
    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);

    UPDATE rc
    SET PostedCredits = rc.CreditsConsumed
    OUTPUT inserted.RunId, inserted.PostedCredits - deleted.PostedCredits INTO @Moved (RunId, Credits)
    FROM dbo.RunCounters rc
    WHERE rc.RunId IN (SELECT Id FROM @Runs)
      AND rc.CreditsConsumed <> rc.PostedCredits;

    MERGE dbo.WorkspaceCreditUsage WITH (HOLDLOCK) AS t
    USING (SELECT d.WorkspaceId, SUM(m.Credits) AS Credits
           FROM @Moved m
           JOIN dbo.Runs r ON r.Id = m.RunId
           JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
           GROUP BY d.WorkspaceId) AS s
    ON t.WorkspaceId = s.WorkspaceId AND t.Day = @Today
    WHEN MATCHED THEN
        UPDATE SET Credits = t.Credits + s.Credits, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (WorkspaceId, Day, Credits) VALUES (s.WorkspaceId, @Today, s.Credits);

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

    -- A test copy is kept only for the runs started from it. Past the cutoff, so a copy whose run is
    -- still being started is never taken.
    DELETE WD
    FROM dbo.WorkflowDefinitions WD
    WHERE WD.Kind = N'Test'
      AND WD.CreatedAt < @CutoffUtc
      AND NOT EXISTS (SELECT 1 FROM dbo.Runs R WHERE R.WorkflowDefinitionId = WD.Id);

    COMMIT TRANSACTION;

    SELECT COUNT(*) FROM @Runs;
END;
GO
