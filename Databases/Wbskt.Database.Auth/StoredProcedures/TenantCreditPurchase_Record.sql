-- Records a credit pack bought with @PaymentId and writes its TenantCreditsGrantedEvent to the outbox in
-- the same transaction, once per provider payment. Returns 1 when it recorded the purchase, 0 when it
-- was already recorded.
CREATE PROCEDURE dbo.TenantCreditPurchase_Record
    @Provider          NVARCHAR(40),
    @PaymentId         NVARCHAR(200),
    @TenantId          INT,
    @CustomerId        NVARCHAR(200),
    @Credits           DECIMAL(18,4),
    @PurchasedAt       DATETIME2(3),
    @ExpiresAt         DATETIME2(3),
    @OutboxMessageType NVARCHAR(256),
    @OutboxPayload     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Recorded INT = 0;

    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantCreditPurchases WITH (UPDLOCK, HOLDLOCK) WHERE Provider = @Provider AND PaymentId = @PaymentId)
    BEGIN
        INSERT INTO dbo.TenantCreditPurchases (Provider, PaymentId, TenantId, CustomerId, Credits, PurchasedAt, ExpiresAt)
        VALUES (@Provider, @PaymentId, @TenantId, @CustomerId, @Credits, @PurchasedAt, @ExpiresAt);

        INSERT INTO dbo.OutboxMessages (MessageType, Payload)
        VALUES (@OutboxMessageType, @OutboxPayload);

        SET @Recorded = 1;

        -- A customer an open tenant pays as again is no longer one to erase at the provider (#951).
        DELETE FROM dbo.BillingCustomerErasures
        WHERE Provider = @Provider
          AND CustomerId = @CustomerId
          AND ErasedAt IS NULL
          AND EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantId AND ClosedAt IS NULL);
    END

    COMMIT TRANSACTION;

    SELECT @Recorded;
END
GO
