-- A workspace's event log, newest first, one page at a time. @CursorId is the Id of the last row of
-- the previous page (NULL for the first); the caller asks for one row more than it shows to learn
-- whether there is a next page. No total count: counting every matching row on every page was the
-- expensive part, and nothing needs it.
CREATE PROCEDURE dbo.EventLog_GetBy_Workspace
    @WorkspaceId INT,
    @EventName NVARCHAR(100) = NULL,
    @Criticality TINYINT = NULL,
    @PolicyId INT = NULL,
    @ClientId INT = NULL,
    @WorkflowId INT = NULL,
    @CursorId BIGINT = NULL,
    @Take INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Take)
        el.Id,
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
      AND (@CursorId IS NULL OR el.Id < @CursorId)
      AND (@PolicyId IS NULL OR el.PolicyId = @PolicyId)
      AND (@ClientId IS NULL OR el.ClientId = @ClientId)
      AND (@WorkflowId IS NULL OR el.WorkflowId = @WorkflowId)
      AND (@EventName IS NULL OR e.EventName LIKE '%' + @EventName + '%')
      AND (@Criticality IS NULL OR e.EventCriticality = @Criticality)
    ORDER BY el.Id DESC;
END
GO
