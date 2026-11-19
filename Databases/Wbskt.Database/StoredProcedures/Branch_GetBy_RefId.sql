CREATE PROCEDURE dbo.Branch_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
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
        -- Always NULL: the previous image's engine still maps it during the rollout. Dropped next release.
        CAST(NULL AS NVARCHAR(MAX)) AS LastOutputJson,
        CreatedAt,
        UpdatedAt,
        RowVersion
    FROM dbo.Branches
    WHERE RefId = @RefId;
END;
GO
