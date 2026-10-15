CREATE PROCEDURE dbo.Run_GetBy_TriggerStartKey
    @TriggerStartKey NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    -- The run an earlier attempt at the same inbound event and registration started, if any, with the
    -- root branch Run_Start created alongside it. Same shape as Run_Start's result, with Created = 0.
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
        r.ParentRunRefId,
        r.OutputJson,
        rb.Id AS RootBranchId,
        CAST(0 AS BIT) AS Created
    FROM dbo.Runs r
    CROSS APPLY (SELECT TOP (1) b.Id
                   FROM dbo.Branches b
                  WHERE b.RunId = r.Id
                    AND b.ParentBranchId IS NULL
                  ORDER BY b.Id) rb
    WHERE r.TriggerStartKey = @TriggerStartKey;
END;
GO
