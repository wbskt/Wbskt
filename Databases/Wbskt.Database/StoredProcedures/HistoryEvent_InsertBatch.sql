CREATE PROCEDURE dbo.HistoryEvent_InsertBatch
    @Events dbo.HistoryEventTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.HistoryEvents
        (RunId, BranchRefId, NodeId, EventKind, Severity, PayloadJson, Timestamp)
    SELECT
        RunId,
        BranchRefId,
        NodeId,
        EventKind,
        Severity,
        PayloadJson,
        Timestamp
    FROM @Events;
END;
GO
