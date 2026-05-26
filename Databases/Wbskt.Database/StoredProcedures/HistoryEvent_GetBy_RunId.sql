CREATE PROCEDURE dbo.HistoryEvent_GetBy_RunId
    @RunId        INT,
    @AfterEventId BIGINT = 0,
    @PageSize     INT    = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@PageSize)
        HistoryEventId,
        RunId,
        BranchRefId,
        NodeId,
        EventKind,
        Severity,
        PayloadJson,
        Timestamp
    FROM dbo.HistoryEvents
    WHERE RunId = @RunId
      AND HistoryEventId > @AfterEventId
    ORDER BY HistoryEventId;
END;
GO
