CREATE PROCEDURE dbo.IdempotencyKey_GetBy_Key
    @KeyValue NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

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
