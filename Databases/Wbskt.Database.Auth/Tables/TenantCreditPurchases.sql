-- Credit packs a tenant bought, one row per provider payment. Recording one writes
-- TenantCreditsGrantedEvent to the outbox in the same transaction (the main database's grant), and a
-- refund, or a chargeback lost, writes TenantCreditGrantRevokedEvent; each happens once however often the
-- webhook arrives. While a chargeback is open the grant is held (HeldAt): TenantCreditGrantHoldChangedEvent
-- carries HoldRevision, raised by every hold and release, so the main database applies them in order.
-- Id orders the rows for the management host's hourly reconcile (dbo.TenantCreditPurchase_GetPage),
-- which repairs a grant any of those events failed to deliver.
CREATE TABLE dbo.TenantCreditPurchases
(
    Provider     NVARCHAR(40)  NOT NULL,
    PaymentId    NVARCHAR(200) NOT NULL,
    TenantId     INT           NOT NULL,
    CustomerId   NVARCHAR(200) NOT NULL,
    Credits      DECIMAL(18,4) NOT NULL,
    PurchasedAt  DATETIME2(3)  NOT NULL,
    ExpiresAt    DATETIME2(3)  NOT NULL,
    RefundedAt   DATETIME2(3)  NULL,
    HeldAt       DATETIME2(3)  NULL,
    HoldRevision INT           NOT NULL CONSTRAINT DF_TenantCreditPurchases_HoldRevision DEFAULT 0,
    CreatedAt    DATETIME2(3)  NOT NULL CONSTRAINT DF_TenantCreditPurchases_CreatedAt DEFAULT SYSUTCDATETIME(),
    Id           INT           IDENTITY (1, 1) NOT NULL,
    CONSTRAINT PK_TenantCreditPurchases PRIMARY KEY (Provider, PaymentId),
    CONSTRAINT UQ_TenantCreditPurchases_Id UNIQUE (Id),
    CONSTRAINT FK_TenantCreditPurchases_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id) ON DELETE CASCADE
);
GO

CREATE NONCLUSTERED INDEX IX_TenantCreditPurchases_TenantId
    ON dbo.TenantCreditPurchases (TenantId, PurchasedAt DESC) INCLUDE (CustomerId);
GO
