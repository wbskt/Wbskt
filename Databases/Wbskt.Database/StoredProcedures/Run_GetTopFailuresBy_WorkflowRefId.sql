CREATE PROCEDURE dbo.Run_GetTopFailuresBy_WorkflowRefId
    @WorkflowRefId UNIQUEIDENTIFIER,
    @FromUtc       DATETIME2(3),
    @ToUtc         DATETIME2(3),
    @Top           INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    -- "Which errors actually happen, and where?" NodeFailed payloads are written with
    -- JsonSerializerDefaults.Web, so the property is camelCase 'errorCode'. JSON_VALUE returns NULL
    -- for anything unparseable, and those rows are dropped rather than bucketed as an empty code.
    SELECT TOP (@Top)
        JSON_VALUE(h.PayloadJson, '$.errorCode') AS ErrorCode,
        h.NodeId                                 AS NodeId,
        COUNT(*)                                 AS Occurrences,
        MAX(h.Timestamp)                         AS LastSeenAt
    FROM dbo.HistoryEvents h
    INNER JOIN dbo.Runs r ON r.Id = h.RunId
    WHERE r.WorkflowRefId = @WorkflowRefId
      AND r.CreatedAt >= @FromUtc
      AND r.CreatedAt < @ToUtc
      AND h.EventKind = N'NodeFailed'
      AND JSON_VALUE(h.PayloadJson, '$.errorCode') IS NOT NULL
    GROUP BY JSON_VALUE(h.PayloadJson, '$.errorCode'), h.NodeId
    ORDER BY COUNT(*) DESC;
END;
GO
