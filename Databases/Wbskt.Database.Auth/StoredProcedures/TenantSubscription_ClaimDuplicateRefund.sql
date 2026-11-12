-- Claims the refund of a duplicate subscription's payment, so it is asked for once: sets
-- DuplicateRefundPaymentId when the subscription is marked a duplicate and nothing was claimed yet, or
-- clears it again (@Release = 1) when the provider refused the refund. DuplicateRefundClaimedAt records
-- when it was claimed: a claim whose refund got no answer is kept, so it is never asked for twice, and an
-- operator finds it by that time. Returns 1 when it changed.
CREATE PROCEDURE dbo.TenantSubscription_ClaimDuplicateRefund
    @Provider          NVARCHAR(40),
    @SubscriptionId    NVARCHAR(200),
    @PaymentId         NVARCHAR(200),
    @Release           BIT = 0,
    @Now               DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @Now = ISNULL(@Now, SYSUTCDATETIME());

    IF @Release = 1
    BEGIN
        UPDATE dbo.TenantSubscriptions
        SET DuplicateRefundPaymentId = NULL,
            DuplicateRefundClaimedAt = NULL
        WHERE Provider = @Provider AND SubscriptionId = @SubscriptionId AND DuplicateRefundPaymentId = @PaymentId;
    END
    ELSE
    BEGIN
        UPDATE dbo.TenantSubscriptions
        SET DuplicateRefundPaymentId = @PaymentId,
            DuplicateRefundClaimedAt = @Now
        WHERE Provider = @Provider AND SubscriptionId = @SubscriptionId
          AND DuplicateAt IS NOT NULL
          AND DuplicateRefundPaymentId IS NULL;
    END

    SELECT @@ROWCOUNT;
END
GO
