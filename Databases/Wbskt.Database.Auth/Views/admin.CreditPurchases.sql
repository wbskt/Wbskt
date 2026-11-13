CREATE VIEW [admin].CreditPurchases
AS
SELECT
    CP.TenantId,
    CP.Provider,
    CP.PaymentId,
    CP.Credits,
    CP.PurchasedAt,
    CP.ExpiresAt,
    CP.RefundedAt,
    CP.HeldAt,
    CP.CreatedAt
FROM dbo.TenantCreditPurchases CP;
GO
