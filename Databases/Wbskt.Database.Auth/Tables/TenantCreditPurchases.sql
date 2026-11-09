-- Credit packs a tenant bought, one row per provider payment. Recording one writes
-- TenantCreditsGrantedEvent to the outbox in the same transaction (the main database's grant), and a
-- refund writes TenantCreditGrantRevokedEvent; each happens once however often the webhook arrives.
CREATE TABLE dbo.TenantCreditPurchases
(
    Provider    NVARCHAR(40)  NOT NULL,
    PaymentId   NVARCHAR(200) NOT NULL,
    TenantId    INT           NOT NULL,
    CustomerId  NVARCHAR(200) NOT NULL,
    Credits     DECIMAL(18,4) NOT NULL,
    PurchasedAt DATETIME2(3)  NOT NULL,
    ExpiresAt   DATETIME2(3)  NOT NULL,
    RefundedAt  DATETIME2(3)  NULL,
    CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_TenantCreditPurchases_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_TenantCreditPurchases PRIMARY KEY (Provider, PaymentId),
    CONSTRAINT FK_TenantCreditPurchases_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id) ON DELETE CASCADE
);
GO

CREATE NONCLUSTERED INDEX IX_TenantCreditPurchases_TenantId
    ON dbo.TenantCreditPurchases (TenantId, PurchasedAt DESC) INCLUDE (CustomerId);
GO
