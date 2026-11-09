-- Records how processing a stored webhook ended. @Processed = 1 marks it done, so a redelivery is answered
-- without acting again; @Error, when set, says why it was set aside (an event this deployment cannot act
-- on) or, with @Processed = 0, why it failed, leaving it to be processed again on the next delivery.
CREATE PROCEDURE dbo.BillingWebhookEvent_Complete
    @Id        BIGINT,
    @Processed BIT,
    @Error     NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.BillingWebhookEvents
    SET ProcessedAt = CASE WHEN @Processed = 1 THEN SYSUTCDATETIME() ELSE ProcessedAt END,
        LastError = @Error
    WHERE Id = @Id;
END
GO
