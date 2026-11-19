-- Each account's subscriptions with the billing provider, ids included so staff can find them there.
-- DuplicateAt marks one found billing beside an older one of the account's: it decides nothing, so the
-- admin's past-due figure leaves it out, as SubscriptionPlan.Decide's callers do.
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
    TS.CreatedAt,
    TS.DuplicateAt
FROM dbo.TenantSubscriptions TS;
GO
