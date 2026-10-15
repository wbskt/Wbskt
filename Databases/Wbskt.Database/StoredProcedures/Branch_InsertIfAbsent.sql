CREATE PROCEDURE dbo.Branch_InsertIfAbsent
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

    -- Insert-only, unlike Branch_Upsert: a Fork replayed after a crash asks for the same children
    -- again (their RefIds are derived from the parent's visit), and an existing child must be left
    -- exactly as it is - it may already be several nodes on. The caller learns from Inserted whether
    -- the row is new, so it counts and dispatches only the children this call created. The unique
    -- constraint on RefId settles a race between two callers, without the range locks HOLDLOCK takes.
    DECLARE @Inserted BIT = 0;

    IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE RefId = @RefId)
    BEGIN
        BEGIN TRY
            INSERT INTO dbo.Branches
                (RefId, RunId, ParentBranchId, ForkCohortId, NodeId,
                 Status, PendingTakePort, LocalJson, LastOutputJson, CompensationStackJson)
            VALUES
                (@RefId, @RunId, @ParentBranchId, @ForkCohortId, @NodeId,
                 @Status, @PendingTakePort, @LocalJson, @LastOutputJson, @CompensationStackJson);

            SET @Inserted = 1;
        END TRY
        BEGIN CATCH
            -- 2601 = unique index, 2627 = unique constraint: another caller inserted it first.
            IF ERROR_NUMBER() NOT IN (2601, 2627)
            BEGIN
                THROW;
            END
        END CATCH
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
        RowVersion,
        @Inserted AS Inserted
    FROM dbo.Branches
    WHERE RefId = @RefId;
END;
GO
