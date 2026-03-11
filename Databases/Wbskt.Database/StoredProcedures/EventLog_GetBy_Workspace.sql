CREATE PROCEDURE dbo.EventLog_GetBy_Workspace
    @WorkspaceId INT,
    @EventName NVARCHAR(100) = NULL,
    @Criticality TINYINT = NULL,
    @Skip INT = 0,
    @Take INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        e.EventName AS EventName,
        el.EventData,
        e.EventCriticality,
        el.CreatedAt,
        COUNT(*) OVER() AS TotalCount
    FROM dbo.EventLogs el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND (@EventName IS NULL OR e.EventName = @EventName)
      AND (@Criticality IS NULL OR e.EventCriticality = @Criticality)
    ORDER BY el.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
