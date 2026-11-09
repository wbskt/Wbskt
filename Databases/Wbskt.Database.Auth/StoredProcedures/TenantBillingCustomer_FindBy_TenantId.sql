-- The tenant's customer id with @Provider, for its billing portal: its subscription's, or else the one it
-- last bought a credit pack as.
CREATE PROCEDURE dbo.TenantBillingCustomer_FindBy_TenantId
    @TenantId INT,
    @Provider NVARCHAR(40)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) CustomerId
    FROM (
        SELECT CustomerId, 0 AS Rank, UpdatedAt AS At
        FROM dbo.TenantSubscriptions
        WHERE TenantId = @TenantId AND Provider = @Provider
        UNION ALL
        SELECT CustomerId, 1, PurchasedAt
        FROM dbo.TenantCreditPurchases
        WHERE TenantId = @TenantId AND Provider = @Provider
    ) c
    ORDER BY Rank, At DESC;
END
GO
