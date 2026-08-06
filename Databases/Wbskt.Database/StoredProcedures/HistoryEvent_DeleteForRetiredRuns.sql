CREATE PROCEDURE dbo.HistoryEvent_DeleteForRetiredRuns
    @CutoffUtc         DATETIME2(3),
    @BatchSize         INT,
    @ElevatedCutoffUtc DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Two windows. Routine Info entries go after the normal retention period; Warn/Error entries -
    -- the record of what went wrong - are kept far longer, but NOT forever: previously they were
    -- excluded outright, so HistoryEvents grew without bound on a failure-heavy workspace.
    -- A NULL elevated cutoff preserves the old keep-forever behaviour for callers that do not pass one.
    DECLARE @Deleted TABLE
    (
        HistoryEventId BIGINT NOT NULL
    );

    ;WITH Targets AS
    (
        SELECT TOP (@BatchSize)
            h.HistoryEventId
        FROM dbo.HistoryEvents h
        INNER JOIN dbo.Runs r ON r.Id = h.RunId
        WHERE r.CompletedAt IS NOT NULL
          AND (
                (h.Severity NOT IN ('Warn', 'Error') AND r.CompletedAt < @CutoffUtc)
                OR
                (h.Severity IN ('Warn', 'Error') AND @ElevatedCutoffUtc IS NOT NULL AND r.CompletedAt < @ElevatedCutoffUtc)
              )
        ORDER BY h.HistoryEventId
    )
    DELETE FROM dbo.HistoryEvents
    OUTPUT DELETED.HistoryEventId INTO @Deleted (HistoryEventId)
    WHERE HistoryEventId IN (SELECT HistoryEventId FROM Targets);

    SELECT COUNT(*)
    FROM @Deleted;
END;
GO
