using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Auth.Host.Services.Billing;

// Dodo Payments' JSON, only the fields read or written here (docs.dodopayments.com/api-reference).

internal sealed record DodoCheckoutRequest(
    [property: JsonPropertyName("product_cart")] IReadOnlyList<DodoCartItem> ProductCart,
    [property: JsonPropertyName("customer")] object Customer,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata,
    [property: JsonPropertyName("return_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReturnUrl,
    [property: JsonPropertyName("feature_flags"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DodoCheckoutFeatureFlags? FeatureFlags);

// Without it Dodo attaches the session to an existing customer with the same email, which may be
// another tenant's.
internal sealed record DodoCheckoutFeatureFlags(
    [property: JsonPropertyName("always_create_new_customer")] bool AlwaysCreateNewCustomer);

internal sealed record DodoExistingCustomer([property: JsonPropertyName("customer_id")] string CustomerId);

internal sealed record DodoCartItem(
    [property: JsonPropertyName("product_id")] string ProductId,
    [property: JsonPropertyName("quantity")] int Quantity);

internal sealed record DodoCheckoutCustomer(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);

internal sealed record DodoCheckoutResponse([property: JsonPropertyName("checkout_url")] string? CheckoutUrl);

internal sealed record DodoPortalResponse([property: JsonPropertyName("link")] string? Link);

internal sealed record DodoCustomer([property: JsonPropertyName("customer_id")] string? CustomerId);

internal sealed record DodoSubscription(
    [property: JsonPropertyName("subscription_id")] string? SubscriptionId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("product_id")] string? ProductId,
    [property: JsonPropertyName("customer")] DodoCustomer? Customer,
    [property: JsonPropertyName("metadata")] Dictionary<string, JsonElement>? Metadata,
    [property: JsonPropertyName("next_billing_date")] DateTimeOffset? NextBillingDate,
    [property: JsonPropertyName("cancel_at_next_billing_date")] bool? CancelAtNextBillingDate,
    [property: JsonPropertyName("scheduled_change")] DodoScheduledChange? ScheduledChange = null);

internal sealed record DodoScheduledChange(
    [property: JsonPropertyName("product_id")] string? ProductId,
    [property: JsonPropertyName("effective_at")] DateTimeOffset? EffectiveAt);

internal sealed record DodoChangePlanRequest(
    [property: JsonPropertyName("product_id")] string ProductId,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("proration_billing_mode")] string ProrationBillingMode,
    [property: JsonPropertyName("effective_at")] string EffectiveAt,
    [property: JsonPropertyName("on_payment_failure"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OnPaymentFailure,
    [property: JsonPropertyName("cancel_scheduled_change_plan"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CancelScheduledChangePlan);

internal sealed record DodoPayment(
    [property: JsonPropertyName("payment_id")] string? PaymentId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("customer")] DodoCustomer? Customer,
    [property: JsonPropertyName("metadata")] Dictionary<string, JsonElement>? Metadata,
    [property: JsonPropertyName("subscription_id")] string? SubscriptionId,
    [property: JsonPropertyName("product_cart")] IReadOnlyList<DodoCartItem>? ProductCart,
    [property: JsonPropertyName("refund_status")] string? RefundStatus,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("disputes")] IReadOnlyList<DodoDispute>? Disputes = null);

internal sealed record DodoDispute(
    [property: JsonPropertyName("dispute_id")] string? DisputeId,
    [property: JsonPropertyName("dispute_status")] string? DisputeStatus,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);

internal sealed record DodoSubscriptionUpdate([property: JsonPropertyName("status")] string Status);

internal sealed record DodoRefundRequest(
    [property: JsonPropertyName("payment_id")] string PaymentId,
    [property: JsonPropertyName("reason")] string Reason);

internal sealed record DodoProduct(
    [property: JsonPropertyName("product_id")] string? ProductId,
    [property: JsonPropertyName("is_recurring")] bool IsRecurring,
    [property: JsonPropertyName("price")] DodoPrice? Price);

// "price" is in the currency's lowest unit (cents for USD); a usage-based price puts it in "fixed_price".
internal sealed record DodoPrice(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("price")] long? Price,
    [property: JsonPropertyName("fixed_price")] long? FixedPrice,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("payment_frequency_count")] int? PaymentFrequencyCount,
    [property: JsonPropertyName("payment_frequency_interval")] string? PaymentFrequencyInterval,
    [property: JsonPropertyName("discount")] decimal? Discount = null,
    [property: JsonPropertyName("discount_bps")] int? DiscountBps = null,
    [property: JsonPropertyName("trial_period_days")] int? TrialPeriodDays = null,
    [property: JsonPropertyName("pay_what_you_want")] bool? PayWhatYouWant = null);
