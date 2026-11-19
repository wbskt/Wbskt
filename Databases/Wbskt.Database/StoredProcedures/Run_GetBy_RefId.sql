CREATE PROCEDURE dbo.Run_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- The workspace is the run's own, or its definition's for a run made before Runs.WorkspaceId was
    -- filled; the fallback goes once the column is NOT NULL (#1059).
    SELECT
        R.Id,
        R.RefId,
        R.WorkflowDefinitionId,
        R.WorkflowRefId,
        R.WorkflowVersion,
        R.TriggerNodeId,
        R.CorrelationKey,
        R.Status,
        R.StartedAt,
        R.CompletedAt,
        R.CancellationRequestedAt,
        R.CancellationReason,
        R.CreditBudget,
        R.CreatedAt,
        R.ParentRunRefId,
        R.OutputJson,
        R.Number,
        R.IsTest,
        COALESCE(R.WorkspaceId, D.WorkspaceId) AS WorkspaceId
    FROM dbo.Runs R
    LEFT JOIN dbo.WorkflowDefinitions D ON D.Id = R.WorkflowDefinitionId
    WHERE R.RefId = @RefId;
END;
GO
