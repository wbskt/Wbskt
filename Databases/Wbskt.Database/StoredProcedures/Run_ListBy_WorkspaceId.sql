CREATE PROCEDURE dbo.Run_ListBy_WorkspaceId
    @WorkspaceId  INT,
    @StatusFilter NVARCHAR(40) = NULL,
    @Top          INT = 50,
    @CursorId     BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Runs are not workspace-scoped themselves; the workspace lives on the definition, so this joins
    -- through WorkflowDefinitionId (covered by IX_Runs_WorkflowDefinitionId_Id).
    SELECT TOP (@Top)
        r.Id,
        r.RefId,
        r.WorkflowDefinitionId,
        r.WorkflowRefId,
        r.WorkflowVersion,
        r.TriggerNodeId,
        r.CorrelationKey,
        r.Status,
        r.StartedAt,
        r.CompletedAt,
        r.CancellationRequestedAt,
        r.CancellationReason,
        r.CreditBudget,
        r.CreatedAt
    FROM dbo.Runs r
    INNER JOIN dbo.WorkflowDefinitions w ON w.Id = r.WorkflowDefinitionId
    WHERE w.WorkspaceId = @WorkspaceId
      AND (@StatusFilter IS NULL OR r.Status = @StatusFilter)
      AND (@CursorId IS NULL OR r.Id < @CursorId)
    ORDER BY r.Id DESC;
END;
GO
