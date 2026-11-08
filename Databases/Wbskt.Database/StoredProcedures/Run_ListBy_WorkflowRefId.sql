CREATE PROCEDURE dbo.Run_ListBy_WorkflowRefId
    @WorkflowRefId UNIQUEIDENTIFIER,
    @StatusFilter NVARCHAR(40) = NULL,
    @Top INT = 50,
    @CursorId BIGINT = NULL,
    -- 1 for test runs only, 0 for none of them; NULL lists both.
    @IsTest BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Waiting is not a stored status: it is a Running run whose every live branch is waiting, as the
    -- stats count it. Its own query, so the branch probes never enter the plan of an ordinary list.
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
        WHERE r.WorkflowRefId = @WorkflowRefId
          -- The IN list is IX_Runs_WorkflowRefId_CorrelationKey_Active's filter word for word, so this reads only the
          -- runs still going instead of every run the workflow ever made.
          AND r.Status IN (N'Running', N'Failing', N'Cancelling')
          AND r.Status = N'Running'
          AND (@CursorId IS NULL OR r.Id < @CursorId)
          AND (@IsTest IS NULL OR r.IsTest = @IsTest)
          AND EXISTS (SELECT 1 FROM dbo.RunParked(r.Id))
        ORDER BY r.Id DESC;
        RETURN;
    END

    SELECT TOP (@Top)
        Id,
        RefId,
        WorkflowDefinitionId,
        WorkflowRefId,
        WorkflowVersion,
        TriggerNodeId,
        CorrelationKey,
        Status,
        StartedAt,
        CompletedAt,
        CancellationRequestedAt,
        CancellationReason,
        CreditBudget,
        CreatedAt,
        ParentRunRefId,
        Number,
        IsTest
    FROM dbo.Runs
    WHERE WorkflowRefId = @WorkflowRefId
      AND (@StatusFilter IS NULL OR Status = @StatusFilter)
      AND (@CursorId IS NULL OR Id < @CursorId)
      AND (@IsTest IS NULL OR IsTest = @IsTest)
    ORDER BY Id DESC;
END;
GO
