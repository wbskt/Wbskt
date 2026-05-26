CREATE PROCEDURE dbo.Branch_GetActiveBy_RunId
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

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
    WHERE RunId = @RunId
      AND Status = N'Active';
END;
GO
