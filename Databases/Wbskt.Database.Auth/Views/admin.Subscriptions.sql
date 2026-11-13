-- Each account's subscriptions with the billing provider, ids included so staff can find them there.
CREATE VIEW [admin].Subscriptions
AS
SELECT
    TS.TenantId,
    TS.Provider,
    TS.SubscriptionId,
    TS.CustomerId,
    TS.PlanCode,
    TS.BillingInterval,
    TS.Status,
    TS.CurrentPeriodEnd,
    TS.CancelAtPeriodEnd,
    TS.PastDueSince,
    TS.ScheduledPlanCode,
    TS.ScheduledBillingInterval,
    TS.ScheduledAt,
    TS.UpdatedAt,
    TS.CreatedAt
FROM dbo.TenantSubscriptions TS;
GO
