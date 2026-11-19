-- Customers at a payment provider whose account here was deleted, so the provider's own record of
-- them (name, address, card fingerprint) can be erased there too. dbo.User_Delete adds one for each
-- customer the deleted account's tenant paid as, unless another open tenant still pays as it.
-- ErasedAt is set once the customer has been erased at the provider; until then the row is the
-- request. The payments themselves stay in TenantSubscriptions and TenantCreditPurchases.
CREATE TABLE dbo.BillingCustomerErasures
(
    Id         INT IDENTITY(1, 1) NOT NULL,
    TenantId   INT           NOT NULL,
    Provider   NVARCHAR(40)  NOT NULL,
    CustomerId NVARCHAR(200) NOT NULL,
    CreatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_BillingCustomerErasures_CreatedAt DEFAULT SYSUTCDATETIME(),
    ErasedAt   DATETIME2(3)  NULL,
    CONSTRAINT PK_BillingCustomerErasures PRIMARY KEY (Id),
    CONSTRAINT UQ_BillingCustomerErasures_Customer UNIQUE (Provider, CustomerId),
    CONSTRAINT FK_BillingCustomerErasures_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id)
);
GO
