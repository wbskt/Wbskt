CREATE PROCEDURE dbo.Branch_Upsert
    @RefId                 UNIQUEIDENTIFIER,
    @RunId                 INT,
    @ParentBranchId        UNIQUEIDENTIFIER,
    @ForkCohortId          UNIQUEIDENTIFIER,
    @NodeId                UNIQUEIDENTIFIER,
    @Status                NVARCHAR(32),
    @PendingTakePort       NVARCHAR(128),
    @LocalJson             NVARCHAR(MAX),
    -- Ignored. Accepted for one release so an engine on the previous image, which still passes it,
    -- keeps working while the hosts restart after the migration; dropped in the release after.
    @LastOutputJson        NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- UPDATE first: targets a single key lock (UPDLOCK prevents dirty reads,
    -- ROWLOCK avoids escalating to a page/table lock under concurrency).
    -- This is deadlock-safe unlike MERGE+HOLDLOCK which takes range locks.
    UPDATE dbo.Branches WITH (UPDLOCK, ROWLOCK)
    SET
        NodeId                = @NodeId,
        Status                = @Status,
        PendingTakePort       = @PendingTakePort,
        WokenAt               = NULL,
        LocalJson             = @LocalJson,
        UpdatedAt             = SYSUTCDATETIME()
    WHERE RefId = @RefId;

    -- INSERT only when no existing row was found.
    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.Branches
            (RefId, RunId, ParentBranchId, ForkCohortId, NodeId,
             Status, PendingTakePort, LocalJson)
        VALUES
            (@RefId, @RunId, @ParentBranchId, @ForkCohortId, @NodeId,
             @Status, @PendingTakePort, @LocalJson);
    END

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
