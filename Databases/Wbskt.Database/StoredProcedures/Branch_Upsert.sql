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

    MERGE dbo.Branches WITH (HOLDLOCK) AS target
    USING (VALUES (
        @RefId, @RunId, @ParentBranchId, @ForkCohortId, @NodeId,
        @Status, @PendingTakePort, @LocalJson, @LastOutputJson, @CompensationStackJson
    )) AS src (RefId, RunId, ParentBranchId, ForkCohortId, NodeId,
               Status, PendingTakePort, LocalJson, LastOutputJson, CompensationStackJson)
    ON target.RefId = src.RefId
    WHEN MATCHED THEN
        UPDATE SET
            NodeId                = src.NodeId,
            Status                = src.Status,
            PendingTakePort       = src.PendingTakePort,
            LocalJson             = src.LocalJson,
            LastOutputJson        = src.LastOutputJson,
            CompensationStackJson = src.CompensationStackJson,
            UpdatedAt             = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (RefId, RunId, ParentBranchId, ForkCohortId, NodeId,
                Status, PendingTakePort, LocalJson, LastOutputJson, CompensationStackJson)
        VALUES (src.RefId, src.RunId, src.ParentBranchId, src.ForkCohortId, src.NodeId,
                src.Status, src.PendingTakePort, src.LocalJson, src.LastOutputJson, src.CompensationStackJson);

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
