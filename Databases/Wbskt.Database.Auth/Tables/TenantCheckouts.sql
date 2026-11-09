-- The plan checkout a tenant last opened with a payment provider, so a second request while it is still
-- open (another tab, another admin) gets the same page instead of a second checkout that could be paid as
-- well. A checkout paid twice anyway is caught by the sync, which cancels and refunds the second
-- subscription. One row per tenant and provider, replaced by the next checkout.
CREATE TABLE dbo.TenantCheckouts
(
    TenantId    INT            NOT NULL,
    Provider    NVARCHAR(40)   NOT NULL,
    ProductId   NVARCHAR(200)  NOT NULL,
    CheckoutUrl NVARCHAR(2000) NOT NULL,
    OpenedAt    DATETIME2(3)   NOT NULL,
    ExpiresAt   DATETIME2(3)   NOT NULL,
    CreatedAt   DATETIME2(3)   NOT NULL CONSTRAINT DF_TenantCheckouts_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_TenantCheckouts PRIMARY KEY (TenantId, Provider),
    CONSTRAINT FK_TenantCheckouts_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id) ON DELETE CASCADE
);
GO
