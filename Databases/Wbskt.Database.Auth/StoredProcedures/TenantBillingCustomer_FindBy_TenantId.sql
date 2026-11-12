-- The customer the tenant pays @Provider as, for its billing portal and its next checkout. Shared is 1
-- when another tenant has paid as the same customer: a checkout before each tenant had its own let the
-- provider match them by the payer's email, and that customer's portal shows the other tenant's
-- subscriptions and card. A customer the tenant pays as alone comes first, so once a checkout has given
-- a tenant a customer of its own it keeps using that one; then a live subscription's (the newest
-- created, not the last synced); then the newest of the rest. An empty id (a payment the provider sent
-- without its customer) is no customer.
CREATE PROCEDURE dbo.TenantBillingCustomer_FindBy_TenantId
    @TenantId INT,
    @Provider NVARCHAR(40)
AS
BEGIN
    SET NOCOUNT ON;

    WITH Candidates AS
    (
        SELECT
            CustomerId,
            CASE WHEN Status IN (N'pending', N'active', N'past_due', N'on_hold') AND DuplicateAt IS NULL THEN 0 ELSE 1 END AS Rank,
            CreatedAt AS At
        FROM dbo.TenantSubscriptions
        WHERE TenantId = @TenantId AND Provider = @Provider AND CustomerId <> N''
        UNION ALL
        SELECT
            CustomerId,
            1,
            PurchasedAt
        FROM dbo.TenantCreditPurchases
        WHERE TenantId = @TenantId AND Provider = @Provider AND CustomerId <> N''
    ),
    Marked AS
    (
        SELECT
            c.CustomerId,
            c.Rank,
            c.At,
            CASE
                WHEN EXISTS (SELECT 1 FROM dbo.TenantSubscriptions s
                             WHERE s.Provider = @Provider AND s.CustomerId = c.CustomerId AND s.TenantId <> @TenantId)
                  OR EXISTS (SELECT 1 FROM dbo.TenantCreditPurchases p
                             WHERE p.Provider = @Provider AND p.CustomerId = c.CustomerId AND p.TenantId <> @TenantId)
                THEN 1 ELSE 0
            END AS Shared
        FROM Candidates c
    )
    SELECT TOP (1)
        CustomerId,
        CAST(Shared AS BIT) AS Shared
    FROM Marked
    ORDER BY Shared, Rank, At DESC;
END
GO
