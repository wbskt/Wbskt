CREATE PROCEDURE dbo.Branch_GetRunning
AS
BEGIN
    SET NOCOUNT ON;

    -- Branches are persisted as 'Active' while executing (never 'Running' - see BranchLoop.ActiveStatus).
    -- 'Compensating' branches are also mid-execution and need re-dispatch after a crash.
    SELECT
        Id,
        RefId,
        RunId,
        ParentBranchId,
        ForkCohortId,
        NodeId,
        Status,
        PendingTakePort,
        LocalJson,
        LastOutputJson,
        CompensationStackJson,
        CreatedAt,
        UpdatedAt,
        RowVersion
    FROM dbo.Branches
    WHERE Status IN (N'Active', N'Compensating');
END;
GO
