-- Every subscription the tenant has had with a payment provider, oldest first.
CREATE PROCEDURE dbo.TenantSubscription_GetBy_TenantId
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        TenantId,
        Provider,
        SubscriptionId,
        CustomerId,
        ProductId,
        PlanCode,
        BillingInterval,
        Status,
        CurrentPeriodEnd,
        CancelAtPeriodEnd,
        PastDueSince,
        RefundedThrough,
        DuplicateAt,
        DuplicateRefundPaymentId,
        ScheduledPlanCode,
        ScheduledBillingInterval,
        ScheduledAt,
        CreatedAt,
        UpdatedAt
    FROM dbo.TenantSubscriptions
    WHERE TenantId = @TenantId
    ORDER BY CreatedAt, SubscriptionId;
END
GO
