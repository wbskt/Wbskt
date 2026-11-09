-- Marks the credit pack bought with @PaymentId refunded and writes its TenantCreditGrantRevokedEvent to
-- the outbox in the same transaction, once. Returns the purchase's TenantId when it was refunded now,
-- nothing when there is no such purchase or it was refunded before.
CREATE PROCEDURE dbo.TenantCreditPurchase_Refund
    @Provider          NVARCHAR(40),
    @PaymentId         NVARCHAR(200),
    @RefundedAt        DATETIME2(3),
    @OutboxMessageType NVARCHAR(256),
    @OutboxPayload     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Refunded TABLE (TenantId INT NOT NULL);

    BEGIN TRANSACTION;

    UPDATE dbo.TenantCreditPurchases
    SET RefundedAt = @RefundedAt
    OUTPUT inserted.TenantId INTO @Refunded
    WHERE Provider = @Provider AND PaymentId = @PaymentId AND RefundedAt IS NULL;

    IF @@ROWCOUNT > 0
    BEGIN
        INSERT INTO dbo.OutboxMessages (MessageType, Payload)
        VALUES (@OutboxMessageType, @OutboxPayload);
    END

    COMMIT TRANSACTION;

    SELECT TenantId FROM @Refunded;
END
GO
