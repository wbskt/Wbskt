-- Writes a tenant's subscription as just read from the provider. PastDueSince is kept from the stored
-- row while the subscription stays past due or on hold, set when it becomes so, and cleared otherwise;
-- RefundedThrough is kept unless @RefundedThrough sets it, and the duplicate marks are always kept. The
-- scheduled change is the provider's, written as read (null for none).
-- Returns the stored row.
CREATE PROCEDURE dbo.TenantSubscription_Upsert
    @TenantId          INT,
    @Provider          NVARCHAR(40),
    @SubscriptionId    NVARCHAR(200),
    @CustomerId        NVARCHAR(200),
    @ProductId         NVARCHAR(200),
    @PlanCode          NVARCHAR(20),
    @BillingInterval   NVARCHAR(20),
    @Status            NVARCHAR(20),
    @CurrentPeriodEnd  DATETIME2(3),
    @CancelAtPeriodEnd BIT,
    @Now               DATETIME2(3),
    @RefundedThrough   DATETIME2(3) = NULL,
    @ScheduledPlanCode        NVARCHAR(20) = NULL,
    @ScheduledBillingInterval NVARCHAR(20) = NULL,
    @ScheduledAt              DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @PastDue BIT = CASE WHEN @Status IN (N'past_due', N'on_hold') THEN 1 ELSE 0 END;

    MERGE dbo.TenantSubscriptions WITH (HOLDLOCK) AS t
    USING (SELECT @Provider AS Provider, @SubscriptionId AS SubscriptionId) AS s
       ON t.Provider = s.Provider AND t.SubscriptionId = s.SubscriptionId
    WHEN MATCHED THEN
        UPDATE SET
            TenantId = @TenantId,
            PastDueSince = CASE
                WHEN @PastDue = 0 THEN NULL
                WHEN t.PastDueSince IS NOT NULL THEN t.PastDueSince
                ELSE @Now END,
            RefundedThrough = COALESCE(@RefundedThrough, t.RefundedThrough),
            CustomerId = @CustomerId,
            ProductId = @ProductId,
            PlanCode = @PlanCode,
            BillingInterval = @BillingInterval,
            Status = @Status,
            CurrentPeriodEnd = @CurrentPeriodEnd,
            CancelAtPeriodEnd = @CancelAtPeriodEnd,
            ScheduledPlanCode = @ScheduledPlanCode,
            ScheduledBillingInterval = @ScheduledBillingInterval,
            ScheduledAt = @ScheduledAt,
            UpdatedAt = @Now
    WHEN NOT MATCHED THEN
        INSERT (TenantId, Provider, SubscriptionId, CustomerId, ProductId, PlanCode, BillingInterval, Status, CurrentPeriodEnd, CancelAtPeriodEnd, PastDueSince, RefundedThrough,
                ScheduledPlanCode, ScheduledBillingInterval, ScheduledAt, UpdatedAt, CreatedAt)
        VALUES (@TenantId, @Provider, @SubscriptionId, @CustomerId, @ProductId, @PlanCode, @BillingInterval, @Status, @CurrentPeriodEnd, @CancelAtPeriodEnd,
                CASE WHEN @PastDue = 1 THEN @Now END, @RefundedThrough, @ScheduledPlanCode, @ScheduledBillingInterval, @ScheduledAt, @Now, @Now);

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
    WHERE Provider = @Provider AND SubscriptionId = @SubscriptionId;
END
GO
