CREATE PROCEDURE dbo.EventLog_GetBy_Workspace
    @WorkspaceId INT,
    @EventName NVARCHAR(100) = NULL,
    @Criticality TINYINT = NULL,
    @Skip INT = 0,
    @Take INT = 50,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Total count of the entire TABLE for this workspace
    SELECT @TotalCount = COUNT(*)
    FROM dbo.EventLogs
    WHERE WorkspaceId = @WorkspaceId;

    -- Filtered and paginated selection
    SELECT 
        e.EventName,
        el.EventData,
        e.EventCriticality,
        el.CreatedAt
    FROM dbo.EventLogs el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND (@EventName IS NULL OR e.EventName LIKE '%' + @EventName + '%')
      AND (@Criticality IS NULL OR e.EventCriticality = @Criticality)
    ORDER BY el.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO
