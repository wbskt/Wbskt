-- One batch of the hourly retention sweep: the oldest @BatchSize keys created before @CutoffUtc,
-- deleted, and how many went. The caller repeats it until a batch comes back short.
CREATE PROCEDURE dbo.IdempotencyKey_DeleteExpired
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Ordered by CreatedAt, so the batch comes straight off IX_IdempotencyKeys_CreatedAt. Ordered by
    -- Id, the plan walked the clustered key from the start until it had a batch, and the last batch of
    -- every sweep (the short one) walked the whole table, LOB columns and all: 19 s of CPU a run on
    -- dev, and past the command timeout on most runs (#1112).
    WITH Targets AS
    (
        SELECT TOP (@BatchSize)
            Id
        FROM dbo.IdempotencyKeys
        WHERE CreatedAt < @CutoffUtc
        ORDER BY CreatedAt
    )
    DELETE FROM Targets;

    SELECT @@ROWCOUNT AS Deleted;
END;
GO
