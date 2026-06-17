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

    BEGIN TRAN;

    INSERT INTO dbo.IdempotencyKeys (KeyValue, RunId, BranchRefId, NodeId, Attempt, Status)
    SELECT @KeyValue, @RunId, @BranchRefId, @NodeId, @Attempt, N'Pending'
    WHERE NOT EXISTS (
        SELECT 1
          FROM dbo.IdempotencyKeys WITH (UPDLOCK, HOLDLOCK)
         WHERE KeyValue = @KeyValue
    );

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

    COMMIT TRAN;
END;
GO
