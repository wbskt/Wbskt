-- A tenant's subscriptions with a payment provider, as last read from the provider (never from a
-- webhook's body: deliveries arrive out of order and twice). Every subscription is kept, so one paid for
-- twice is never lost from view; which one decides the tenant's plan is SubscriptionPlan.Deciding, and
-- the plan itself stays in dbo.Tenants.
--
-- CurrentPeriodEnd is the provider's next billing date, the end of what has been paid for.
-- PastDueSince is when a renewal first failed, for the grace period before the plan drops to Free.
-- RefundedThrough is the period end at the time a subscription payment was refunded or lost to a
-- chargeback: the paid plan is withheld until the subscription is paid past it again.
-- DuplicateAt is when this subscription was found billing beside an older one of the tenant's and
-- cancelled; DuplicateRefundPaymentId is its payment refunded for that, so it is refunded once.
-- ScheduledPlanCode and ScheduledBillingInterval are a downgrade the provider will make at ScheduledAt
-- (the end of the period paid for), until then the subscription stays on PlanCode.
CREATE TABLE dbo.TenantSubscriptions
(
    TenantId                 INT           NOT NULL,
    Provider                 NVARCHAR(40)  NOT NULL,
    SubscriptionId           NVARCHAR(200) NOT NULL,
    CustomerId               NVARCHAR(200) NOT NULL,
    ProductId                NVARCHAR(200) NOT NULL,
    PlanCode                 NVARCHAR(20)  NOT NULL,
    BillingInterval          NVARCHAR(20)  NOT NULL,
    Status                   NVARCHAR(20)  NOT NULL,
    CurrentPeriodEnd         DATETIME2(3)  NOT NULL,
    CancelAtPeriodEnd        BIT           NOT NULL,
    PastDueSince             DATETIME2(3)  NULL,
    RefundedThrough          DATETIME2(3)  NULL,
    DuplicateAt              DATETIME2(3)  NULL,
    DuplicateRefundPaymentId NVARCHAR(200) NULL,
    ScheduledPlanCode        NVARCHAR(20)  NULL,
    ScheduledBillingInterval NVARCHAR(20)  NULL,
    ScheduledAt              DATETIME2(3)  NULL,
    UpdatedAt                DATETIME2(3)  NOT NULL CONSTRAINT DF_TenantSubscriptions_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt                DATETIME2(3)  NOT NULL CONSTRAINT DF_TenantSubscriptions_CreatedAt DEFAULT SYSUTCDATETIME(),
    -- When DuplicateRefundPaymentId was claimed. A claim whose refund got no answer is kept rather than
    -- asked for again, which could refund twice; this is how an operator finds it.
    DuplicateRefundClaimedAt DATETIME2(3)  NULL,
    CONSTRAINT PK_TenantSubscriptions PRIMARY KEY (Provider, SubscriptionId),
    CONSTRAINT FK_TenantSubscriptions_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id) ON DELETE CASCADE
);
GO

CREATE NONCLUSTERED INDEX IX_TenantSubscriptions_TenantId
    ON dbo.TenantSubscriptions (TenantId);
GO

-- Finds another tenant paying as the same customer (dbo.TenantBillingCustomer_FindBy_TenantId).
CREATE NONCLUSTERED INDEX IX_TenantSubscriptions_CustomerId
    ON dbo.TenantSubscriptions (Provider, CustomerId) INCLUDE (TenantId);
GO
