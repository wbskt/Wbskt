-- Records the plan checkout the tenant just opened, replacing the one before.
CREATE PROCEDURE dbo.TenantCheckout_Record
    @TenantId    INT,
    @Provider    NVARCHAR(40),
    @ProductId   NVARCHAR(200),
    @CheckoutUrl NVARCHAR(2000),
    @OpenedAt    DATETIME2(3),
    @ExpiresAt   DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.TenantCheckouts WITH (HOLDLOCK) AS t
    USING (SELECT @TenantId AS TenantId, @Provider AS Provider) AS s
       ON t.TenantId = s.TenantId AND t.Provider = s.Provider
    WHEN MATCHED THEN
        UPDATE SET ProductId = @ProductId, CheckoutUrl = @CheckoutUrl, OpenedAt = @OpenedAt, ExpiresAt = @ExpiresAt
    WHEN NOT MATCHED THEN
        INSERT (TenantId, Provider, ProductId, CheckoutUrl, OpenedAt, ExpiresAt)
        VALUES (@TenantId, @Provider, @ProductId, @CheckoutUrl, @OpenedAt, @ExpiresAt);
END
GO
