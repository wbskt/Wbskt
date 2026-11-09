-- The tenant a credit pack bought with @PaymentId was for, and whether it was refunded.
CREATE PROCEDURE dbo.TenantCreditPurchase_FindBy_PaymentId
    @Provider  NVARCHAR(40),
    @PaymentId NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        TenantId,
        Credits,
        RefundedAt
    FROM dbo.TenantCreditPurchases
    WHERE Provider = @Provider AND PaymentId = @PaymentId;
END
GO
