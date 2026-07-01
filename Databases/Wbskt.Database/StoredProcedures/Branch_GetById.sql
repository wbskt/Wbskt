CREATE PROCEDURE dbo.Branch_GetById
    @Id INT
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
    WHERE Id = @Id;
END;
GO
