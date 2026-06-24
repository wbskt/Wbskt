CREATE PROCEDURE dbo.Branch_Upsert
    @RefId                 UNIQUEIDENTIFIER,
    @RunId                 INT,
    @ParentBranchId        UNIQUEIDENTIFIER,
    @ForkCohortId          UNIQUEIDENTIFIER,
    @NodeId                UNIQUEIDENTIFIER,
    @Status                NVARCHAR(32),
    @PendingTakePort       NVARCHAR(128),
    @LocalJson             NVARCHAR(MAX),
    @LastOutputJson        NVARCHAR(MAX),
    @CompensationStackJson NVARCHAR(MAX)
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
        LocalJson             = @LocalJson,
        LastOutputJson        = @LastOutputJson,
        CompensationStackJson = @CompensationStackJson,
        UpdatedAt             = SYSUTCDATETIME()
    WHERE RefId = @RefId;

    -- INSERT only when no existing row was found.
    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.Branches
            (RefId, RunId, ParentBranchId, ForkCohortId, NodeId,
             Status, PendingTakePort, LocalJson, LastOutputJson, CompensationStackJson)
        VALUES
            (@RefId, @RunId, @ParentBranchId, @ForkCohortId, @NodeId,
             @Status, @PendingTakePort, @LocalJson, @LastOutputJson, @CompensationStackJson);
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
        LastOutputJson,
        CompensationStackJson,
        CreatedAt,
        UpdatedAt,
        RowVersion
    FROM dbo.Branches
    WHERE RefId = @RefId;
END;
GO
