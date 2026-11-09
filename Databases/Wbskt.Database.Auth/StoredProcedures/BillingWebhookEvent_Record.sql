-- Stores a delivered webhook once per provider and event id, and returns its row: Id, and ProcessedAt
-- (not NULL when an earlier delivery was already acted on, so this one need not be). Each call counts an
-- attempt.
CREATE PROCEDURE dbo.BillingWebhookEvent_Record
    @Provider  NVARCHAR(40),
    @EventId   NVARCHAR(200),
    @EventType NVARCHAR(100),
    @Payload   NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.BillingWebhookEvents WITH (UPDLOCK, HOLDLOCK) WHERE Provider = @Provider AND EventId = @EventId)
    BEGIN
        INSERT INTO dbo.BillingWebhookEvents (Provider, EventId, EventType, Payload)
        VALUES (@Provider, @EventId, @EventType, @Payload);
    END

    UPDATE dbo.BillingWebhookEvents
    SET Attempts = Attempts + 1
    OUTPUT inserted.Id, inserted.ProcessedAt
    WHERE Provider = @Provider AND EventId = @EventId;

    COMMIT TRANSACTION;
END
GO
