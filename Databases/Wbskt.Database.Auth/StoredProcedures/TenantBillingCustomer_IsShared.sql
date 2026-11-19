-- Whether an open tenant other than @TenantId has paid @Provider as @CustomerId (see
-- dbo.TenantBillingCustomer_FindBy_TenantId). Read through the (Provider, CustomerId) indexes. A closed
-- tenant (a deleted account's, #951) no longer counts: nobody can use its portal.
CREATE PROCEDURE dbo.TenantBillingCustomer_IsShared
    @Provider   NVARCHAR(40),
    @CustomerId NVARCHAR(200),
    @TenantId   INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(CASE
        WHEN @CustomerId = N'' THEN 0
        WHEN EXISTS (SELECT 1 FROM dbo.TenantSubscriptions S INNER JOIN dbo.Tenants T ON T.Id = S.TenantId
                     WHERE S.Provider = @Provider AND S.CustomerId = @CustomerId AND S.TenantId <> @TenantId AND T.ClosedAt IS NULL)
          OR EXISTS (SELECT 1 FROM dbo.TenantCreditPurchases P INNER JOIN dbo.Tenants T ON T.Id = P.TenantId
                     WHERE P.Provider = @Provider AND P.CustomerId = @CustomerId AND P.TenantId <> @TenantId AND T.ClosedAt IS NULL)
        THEN 1 ELSE 0 END AS BIT) AS Shared;
END
GO
