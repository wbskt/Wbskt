-- Every credit pack bought, in Id order a page at a time after @AfterId, with its refund and hold: what
-- the management host reconciles the main database's grants against (CreditGrantReconcile), so a grant
-- whose event never arrived is repaired.
CREATE PROCEDURE dbo.TenantCreditPurchase_GetPage
    @AfterId INT = NULL,
    @Take    INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Take)
        p.Id,
        p.TenantId,
        t.RefId AS TenantRefId,
        p.Provider,
        p.PaymentId,
        p.Credits,
        p.PurchasedAt,
        p.ExpiresAt,
        p.RefundedAt,
        p.HeldAt,
        p.HoldRevision
    FROM dbo.TenantCreditPurchases p
    JOIN dbo.Tenants t ON t.Id = p.TenantId
    WHERE @AfterId IS NULL OR p.Id > @AfterId
    ORDER BY p.Id;
END
GO
