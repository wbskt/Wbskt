CREATE PROCEDURE dbo.Run_GetActiveChildrenBy_ParentRefId
    @ParentRunRefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- The children a cancelled or failed parent still has going, for the cancel cascade. A child that
    -- has already finished is left alone: its result has been delivered, or dropped with the parent's
    -- bookmarks. IX_Runs_ParentRunRefId_Active covers this.
    SELECT
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
        r.ParentRunRefId
    FROM dbo.Runs r
    WHERE r.ParentRunRefId = @ParentRunRefId
      AND r.Status IN (N'Running', N'Failing', N'Cancelling')
    ORDER BY r.Id;
END;
GO
