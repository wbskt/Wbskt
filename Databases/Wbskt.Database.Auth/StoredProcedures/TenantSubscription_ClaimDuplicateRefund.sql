-- Claims the refund of a duplicate subscription's payment, so it is asked for once: sets
-- DuplicateRefundPaymentId when the subscription is marked a duplicate and nothing was claimed yet, or
-- clears it again (@Release = 1) when the refund failed. A claim on the same payment older than
-- @StaleAfterSeconds is taken again: its refund was never answered (the host stopped mid-call), and the
-- caller asks only while the provider still shows the payment as not refunded. Returns 1 when it changed.
CREATE PROCEDURE dbo.TenantSubscription_ClaimDuplicateRefund
    @Provider          NVARCHAR(40),
    @SubscriptionId    NVARCHAR(200),
    @PaymentId         NVARCHAR(200),
    @Release           BIT = 0,
    @Now               DATETIME2(3) = NULL,
    @StaleAfterSeconds INT = 900
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
          AND (DuplicateRefundPaymentId IS NULL
               OR (DuplicateRefundPaymentId = @PaymentId
                   AND (DuplicateRefundClaimedAt IS NULL OR DuplicateRefundClaimedAt <= DATEADD(SECOND, -@StaleAfterSeconds, @Now))));
    END

    SELECT @@ROWCOUNT;
END
GO
