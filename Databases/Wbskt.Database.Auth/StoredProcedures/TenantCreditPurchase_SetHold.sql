-- Holds the credit pack bought with @PaymentId while a chargeback on it is open (@Held = 1), or releases
-- it once the chargeback is won or withdrawn (@Held = 0), and writes TenantCreditGrantHoldChangedEvent to
-- the outbox in the same transaction. Only from @ExpectedRevision, and only for a pack not refunded: a
-- change worked out from a stale read is refused. Returns 1 when it changed the hold.
CREATE PROCEDURE dbo.TenantCreditPurchase_SetHold
    @Provider          NVARCHAR(40),
    @PaymentId         NVARCHAR(200),
    @Held              BIT,
    @At                DATETIME2(3),
    @ExpectedRevision  INT,
    @OutboxMessageType NVARCHAR(256),
    @OutboxPayload     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Changed INT;

    BEGIN TRANSACTION;

    UPDATE dbo.TenantCreditPurchases
    SET HeldAt = CASE WHEN @Held = 1 THEN @At END,
        HoldRevision = HoldRevision + 1
    WHERE Provider = @Provider
      AND PaymentId = @PaymentId
      AND RefundedAt IS NULL
      AND HoldRevision = @ExpectedRevision
      AND (CASE WHEN HeldAt IS NULL THEN 0 ELSE 1 END) <> @Held;

    SET @Changed = @@ROWCOUNT;

    IF @Changed > 0
    BEGIN
        INSERT INTO dbo.OutboxMessages (MessageType, Payload)
        VALUES (@OutboxMessageType, @OutboxPayload);
    END

    COMMIT TRANSACTION;

    SELECT @Changed;
END
GO
