-- The page of the tenant's plan checkout for @ProductId, while it is still open.
CREATE PROCEDURE dbo.TenantCheckout_FindOpen
    @TenantId  INT,
    @Provider  NVARCHAR(40),
    @ProductId NVARCHAR(200),
    @Now       DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CheckoutUrl
    FROM dbo.TenantCheckouts
    WHERE TenantId = @TenantId
      AND Provider = @Provider
      AND ProductId = @ProductId
      AND ExpiresAt > @Now;
END
GO
