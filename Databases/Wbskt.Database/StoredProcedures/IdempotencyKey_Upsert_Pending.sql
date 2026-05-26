CREATE PROCEDURE dbo.IdempotencyKey_Upsert_Pending
    @KeyValue    NVARCHAR(200),
    @RunId       INT,
    @BranchRefId UNIQUEIDENTIFIER,
    @NodeId      UNIQUEIDENTIFIER,
    @Attempt     INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.IdempotencyKeys WHERE KeyValue = @KeyValue)
    BEGIN
        INSERT INTO dbo.IdempotencyKeys
            (KeyValue, RunId, BranchRefId, NodeId, Attempt, Status)
        VALUES
            (@KeyValue, @RunId, @BranchRefId, @NodeId, @Attempt, N'Pending');
    END;

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
