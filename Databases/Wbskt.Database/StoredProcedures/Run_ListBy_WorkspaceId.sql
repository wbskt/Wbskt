CREATE PROCEDURE dbo.Run_ListBy_WorkspaceId
    @WorkspaceId  INT,
    @StatusFilter NVARCHAR(40) = NULL,
    @Top          INT = 50,
    @CursorId     BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Waiting is derived, not stored; see Run_ListBy_WorkflowRefId.
    IF @StatusFilter = N'Waiting'
    BEGIN
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
            r.CreatedAt,
            r.ParentRunRefId,
            r.Number
        FROM dbo.Runs r
        INNER JOIN dbo.WorkflowDefinitions w ON w.Id = r.WorkflowDefinitionId
        WHERE w.WorkspaceId = @WorkspaceId
          -- The IN list is IX_Runs_WorkflowDefinitionId_Active's filter word for word, so this reads only the
          -- runs still going instead of every run the workspace ever made.
          AND r.Status IN (N'Running', N'Failing', N'Cancelling')
          AND r.Status = N'Running'
          AND (@CursorId IS NULL OR r.Id < @CursorId)
          AND EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.RunId = r.Id AND b.Status = N'Waiting')
          AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.RunId = r.Id AND b.Status = N'Active')
        ORDER BY r.Id DESC;
        RETURN;
    END

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
        r.CreatedAt,
        r.ParentRunRefId,
        r.Number
    FROM dbo.Runs r
    INNER JOIN dbo.WorkflowDefinitions w ON w.Id = r.WorkflowDefinitionId
    WHERE w.WorkspaceId = @WorkspaceId
      AND (@StatusFilter IS NULL OR r.Status = @StatusFilter)
      AND (@CursorId IS NULL OR r.Id < @CursorId)
    ORDER BY r.Id DESC;
END;
GO
