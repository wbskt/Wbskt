CREATE PROCEDURE dbo.Run_ListBy_WorkspaceId
    @WorkspaceId  INT,
    @StatusFilter NVARCHAR(40) = NULL,
    @Top          INT = 50,
    @CursorId     BIGINT = NULL,
    -- 1 for test runs only, 0 for none of them; NULL lists both.
    @IsTest       BIT = NULL
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
            r.Number,
            r.IsTest
        FROM dbo.Runs r
        WHERE r.WorkspaceId = @WorkspaceId
          -- The IN list is IX_Runs_WorkspaceId_Active's filter word for word, so this reads only the
          -- runs still going instead of every run the workspace ever made.
          AND r.Status IN (N'Running', N'Failing', N'Cancelling')
          AND r.Status = N'Running'
          AND (@CursorId IS NULL OR r.Id < @CursorId)
          AND (@IsTest IS NULL OR r.IsTest = @IsTest)
          AND EXISTS (SELECT 1 FROM dbo.RunParked(r.Id))
        ORDER BY r.Id DESC;
        RETURN;
    END

    -- Covered by IX_Runs_WorkspaceId_Id.
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
        r.Number,
        r.IsTest
    FROM dbo.Runs r
    WHERE r.WorkspaceId = @WorkspaceId
      AND (@StatusFilter IS NULL OR r.Status = @StatusFilter)
      AND (@CursorId IS NULL OR r.Id < @CursorId)
      AND (@IsTest IS NULL OR r.IsTest = @IsTest)
    ORDER BY r.Id DESC;
END;
GO
