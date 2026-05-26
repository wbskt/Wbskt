CREATE PROCEDURE dbo.HistoryEvent_DeleteForRetiredRuns
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

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
          AND r.CompletedAt < @CutoffUtc
        ORDER BY h.HistoryEventId
    )
    DELETE FROM dbo.HistoryEvents
    OUTPUT DELETED.HistoryEventId INTO @Deleted (HistoryEventId)
    WHERE HistoryEventId IN (SELECT HistoryEventId FROM Targets);

    SELECT COUNT(*)
    FROM @Deleted;
END;
GO
