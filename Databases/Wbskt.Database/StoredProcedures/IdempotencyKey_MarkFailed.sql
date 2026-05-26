CREATE PROCEDURE dbo.IdempotencyKey_MarkFailed
    @KeyValue  NVARCHAR(200),
    @ErrorJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.IdempotencyKeys
    SET Status      = N'Failed',
        ErrorJson   = @ErrorJson,
        CompletedAt = SYSUTCDATETIME()
    WHERE KeyValue = @KeyValue;

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
