CREATE PROCEDURE dbo.IdempotencyKey_MarkSucceeded
    @KeyValue   NVARCHAR(200),
    @ResultJson NVARCHAR(MAX),
    @ClaimToken UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.IdempotencyKeys
    SET Status      = N'Succeeded',
        ResultJson  = @ResultJson,
        CompletedAt = SYSUTCDATETIME()
    WHERE KeyValue = @KeyValue
      -- An event claim is settled only by the attempt that holds it, so a late write from an engine
      -- whose claim was released and taken over cannot overwrite the new holder's. Action keys pass none.
      AND (@ClaimToken IS NULL OR BranchRefId = @ClaimToken);

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
