CREATE PROCEDURE dbo.Branch_Update
    @Id             INT,
    @NodeId         UNIQUEIDENTIFIER,
    @Status         NVARCHAR(32),
    @LocalJson      NVARCHAR(MAX),
    @LastOutputJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Branches
    SET
        NodeId = COALESCE(@NodeId, NodeId),
        Status = @Status,
        LocalJson = COALESCE(@LocalJson, LocalJson),
        LastOutputJson = @LastOutputJson,
        PendingTakePort = NULL,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id;

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
