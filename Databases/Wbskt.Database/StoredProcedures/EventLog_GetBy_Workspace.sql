CREATE PROCEDURE dbo.EventLog_GetBy_Workspace
    @WorkspaceId INT,
    @EventName NVARCHAR(100) = NULL,
    @Criticality TINYINT = NULL,
    @PolicyId INT = NULL,
    @ClientId INT = NULL,
    @WorkflowId INT = NULL,
    @Skip INT = 0,
    @Take INT = 50,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Total count with filters applied
    SELECT @TotalCount = COUNT(*)
    FROM dbo.EventLogs el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND (@PolicyId IS NULL OR el.PolicyId = @PolicyId)
      AND (@ClientId IS NULL OR el.ClientId = @ClientId)
      AND (@WorkflowId IS NULL OR el.WorkflowId = @WorkflowId)
      AND (@Criticality IS NULL OR e.EventCriticality = @Criticality)
      AND (@EventName IS NULL OR e.EventName LIKE '%' + @EventName + '%');

    -- Filtered and paginated selection
    SELECT 
        e.EventName,
        el.EventData,
        e.EventCriticality,
        el.PolicyRefId,
        el.ClientRefId,
        el.WorkflowRefId,
        el.CreatedAt
    FROM dbo.EventLogs el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND (@PolicyId IS NULL OR el.PolicyId = @PolicyId)
      AND (@ClientId IS NULL OR el.ClientId = @ClientId)
      AND (@WorkflowId IS NULL OR el.WorkflowId = @WorkflowId)
      AND (@EventName IS NULL OR e.EventName LIKE '%' + @EventName + '%')
      AND (@Criticality IS NULL OR e.EventCriticality = @Criticality)
    ORDER BY el.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO
