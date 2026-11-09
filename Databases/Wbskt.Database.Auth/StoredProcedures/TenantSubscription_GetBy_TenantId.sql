-- The tenant's subscription with a payment provider, if it has one.
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
        UpdatedAt
    FROM dbo.TenantSubscriptions
    WHERE TenantId = @TenantId;
END
GO
