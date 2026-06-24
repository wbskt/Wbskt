CREATE PROCEDURE dbo.IdempotencyKey_Upsert_Pending
    @KeyValue    NVARCHAR(200),
    @RunId       INT,
    @BranchRefId UNIQUEIDENTIFIER,
    @NodeId      UNIQUEIDENTIFIER,
    @Attempt     INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Attempt insert directly. Under high concurrency, we avoid checking existence beforehand
    -- with range/key locks (UPDLOCK, HOLDLOCK) which cause severe SQL deadlocks.
    -- If another thread already inserted this key, we catch the unique constraint error
    -- (2601/2627), ignore it, and proceed to SELECT the existing row.
    BEGIN TRY
        INSERT INTO dbo.IdempotencyKeys (KeyValue, RunId, BranchRefId, NodeId, Attempt, Status)
        VALUES (@KeyValue, @RunId, @BranchRefId, @NodeId, @Attempt, N'Pending');
    END TRY
    BEGIN CATCH
        -- Suppress duplicate key violation errors (2601 = Unique Index, 2627 = Unique Constraint)
        IF ERROR_NUMBER() NOT IN (2601, 2627)
        BEGIN
            THROW;
        END
    END CATCH;

    SELECT
        Id,
        KeyValue,
        RunId,
        BranchRefId,
        NodeId,
        Attempt,
        Status,
        ResultJson,
        ErrorJson,
        CreatedAt,
        CompletedAt
      FROM dbo.IdempotencyKeys
     WHERE KeyValue = @KeyValue;
END;
GO
