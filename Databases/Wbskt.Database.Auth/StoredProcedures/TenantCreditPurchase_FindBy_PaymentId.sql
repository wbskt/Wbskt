-- The tenant a credit pack bought with @PaymentId was for, whether it was refunded, and its hold.
CREATE PROCEDURE dbo.TenantCreditPurchase_FindBy_PaymentId
    @Provider  NVARCHAR(40),
    @PaymentId NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        TenantId,
        Credits,
        RefundedAt,
        HeldAt,
        HoldRevision
    FROM dbo.TenantCreditPurchases
    WHERE Provider = @Provider AND PaymentId = @PaymentId;
END
GO
