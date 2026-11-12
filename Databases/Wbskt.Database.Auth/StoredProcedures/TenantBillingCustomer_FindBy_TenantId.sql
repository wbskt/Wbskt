-- The tenant's customer id with @Provider, for its billing portal and its next checkout: its
-- subscription's, or else the one it last bought a credit pack as. Shared is 1 when another tenant has
-- paid as the same customer: a checkout before each tenant had its own customer let the provider match
-- them by email, and that customer's portal would show the other tenant's subscriptions and card.
CREATE PROCEDURE dbo.TenantBillingCustomer_FindBy_TenantId
    @TenantId INT,
    @Provider NVARCHAR(40)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1)
        c.CustomerId,
        CAST(CASE
            WHEN EXISTS (SELECT 1 FROM dbo.TenantSubscriptions s
                         WHERE s.Provider = @Provider AND s.CustomerId = c.CustomerId AND s.TenantId <> @TenantId)
              OR EXISTS (SELECT 1 FROM dbo.TenantCreditPurchases p
                         WHERE p.Provider = @Provider AND p.CustomerId = c.CustomerId AND p.TenantId <> @TenantId)
            THEN 1 ELSE 0 END AS BIT) AS Shared
    FROM (
        SELECT CustomerId, 0 AS Rank, UpdatedAt AS At
        FROM dbo.TenantSubscriptions
        WHERE TenantId = @TenantId AND Provider = @Provider
        UNION ALL
        SELECT CustomerId, 1, PurchasedAt
        FROM dbo.TenantCreditPurchases
        WHERE TenantId = @TenantId AND Provider = @Provider
    ) c
    ORDER BY c.Rank, c.At DESC;
END
GO
