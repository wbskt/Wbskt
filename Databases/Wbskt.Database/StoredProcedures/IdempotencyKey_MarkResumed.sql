CREATE PROCEDURE dbo.IdempotencyKey_MarkResumed
    @KeyValue   NVARCHAR(200),
    @ClaimToken UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- Records on a still-Pending inbound-event claim that the event has woken at least one waiting
    -- branch. A redelivery that reclaims the claim after a stop then finds those bookmarks gone, and
    -- this tells it the event was a wake, not something to start trigger runs for.
    UPDATE dbo.IdempotencyKeys
    SET ResultJson = N'{"resumed":true}'
    WHERE KeyValue = @KeyValue
      AND BranchRefId = @ClaimToken
      AND Status = N'Pending';
END;
GO
