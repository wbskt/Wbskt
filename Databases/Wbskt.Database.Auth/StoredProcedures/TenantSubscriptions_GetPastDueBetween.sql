-- Subscriptions still behind on payment that fell behind between @From and @To: the ones whose grace
-- period ends soon, for the notice that says so. A duplicate is left out; it is cancelled, not chased.
CREATE PROCEDURE dbo.TenantSubscriptions_GetPastDueBetween
    @From DATETIME2(3),
    @To   DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        TenantId,
        SubscriptionId,
        PlanCode,
        PastDueSince
    FROM dbo.TenantSubscriptions
    WHERE Status IN (N'past_due', N'on_hold')
      AND DuplicateAt IS NULL
      AND PastDueSince > @From
      AND PastDueSince <= @To;
END
GO
