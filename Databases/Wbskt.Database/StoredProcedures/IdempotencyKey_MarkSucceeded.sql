CREATE PROCEDURE dbo.IdempotencyKey_MarkSucceeded
    @KeyValue   NVARCHAR(200),
    @ResultJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.IdempotencyKeys
    SET Status      = N'Succeeded',
        ResultJson  = @ResultJson,
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
