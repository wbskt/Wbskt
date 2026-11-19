CREATE PROCEDURE dbo.Branch_Update
    @Id             BIGINT,
    @NodeId         UNIQUEIDENTIFIER,
    @Status         NVARCHAR(32),
    @LocalJson      NVARCHAR(MAX),
    -- Ignored. Accepted for one release so an engine on the previous image, which still passes it,
    -- keeps working while the hosts restart after the migration; dropped in the release after (#1059).
    @LastOutputJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Branches
    SET
        NodeId = COALESCE(@NodeId, NodeId),
        Status = @Status,
        LocalJson = COALESCE(@LocalJson, LocalJson),
        PendingTakePort = NULL,
        WokenAt = NULL,
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
        -- Always NULL: the previous image's engine still maps it during the rollout. Dropped next release.
        CAST(NULL AS NVARCHAR(MAX)) AS LastOutputJson,
        CreatedAt,
        UpdatedAt,
        RowVersion
    FROM dbo.Branches
    WHERE Id = @Id;
END;
GO
