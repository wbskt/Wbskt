-- Whether a tenant other than @TenantId has paid @Provider as @CustomerId (see
-- dbo.TenantBillingCustomer_FindBy_TenantId). Read through the (Provider, CustomerId) indexes.
CREATE PROCEDURE dbo.TenantBillingCustomer_IsShared
    @Provider   NVARCHAR(40),
    @CustomerId NVARCHAR(200),
    @TenantId   INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(CASE
        WHEN @CustomerId = N'' THEN 0
        WHEN EXISTS (SELECT 1 FROM dbo.TenantSubscriptions
                     WHERE Provider = @Provider AND CustomerId = @CustomerId AND TenantId <> @TenantId)
          OR EXISTS (SELECT 1 FROM dbo.TenantCreditPurchases
                     WHERE Provider = @Provider AND CustomerId = @CustomerId AND TenantId <> @TenantId)
        THEN 1 ELSE 0 END AS BIT) AS Shared;
END
GO
