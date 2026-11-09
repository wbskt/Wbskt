-- The billing emails each tenant has been sent, one row per kind and period (a month, a payment, a
-- plan revision), so a notice raised twice, by a redelivered event or a worker's next tick, is sent
-- once (dbo.BillingNotice_Claim). Rows are never updated.
CREATE TABLE dbo.BillingNotices
(
    TenantId  INT           NOT NULL,
    Kind      NVARCHAR(40)  NOT NULL,
    PeriodKey NVARCHAR(200) NOT NULL,
    CreatedAt DATETIME2(3)  NOT NULL CONSTRAINT DF_BillingNotices_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BillingNotices PRIMARY KEY (TenantId, Kind, PeriodKey),
    CONSTRAINT FK_BillingNotices_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id) ON DELETE CASCADE
);
GO
