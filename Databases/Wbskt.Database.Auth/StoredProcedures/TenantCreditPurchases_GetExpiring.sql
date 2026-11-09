-- Credit packs that expire between @From and @To and still count: not refunded, not held over a
-- chargeback. For the notice that a pack runs out soon.
CREATE PROCEDURE dbo.TenantCreditPurchases_GetExpiring
    @From DATETIME2(3),
    @To   DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        TenantId,
        PaymentId,
        Credits,
        ExpiresAt
    FROM dbo.TenantCreditPurchases
    WHERE ExpiresAt > @From
      AND ExpiresAt <= @To
      AND RefundedAt IS NULL
      AND HeldAt IS NULL;
END
GO
