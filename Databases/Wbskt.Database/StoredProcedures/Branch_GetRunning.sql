CREATE PROCEDURE dbo.Branch_GetRunning
AS
BEGIN
    SET NOCOUNT ON;

    -- Branches are persisted as 'Active' while executing (never 'Running' - see BranchLoop.ActiveStatus).
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
        CreatedAt,
        UpdatedAt,
        RowVersion
    FROM dbo.Branches
    WHERE Status = N'Active';
END;
GO
