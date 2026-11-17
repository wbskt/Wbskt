using System.Net;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// What a failed call to the payment provider (an <see cref="HttpRequestException"/> from
/// <see cref="IPaymentGateway"/>, a timeout included) means: whether it certainly did nothing, and what
/// the caller is told.
/// </summary>
internal static class ProviderFailure
{
    private static readonly Error Unavailable = Error.Unavailable("BILLING_PROVIDER_UNAVAILABLE", "The payment provider could not be reached. Try again shortly.");

    private static readonly Error Refused = Error.Conflict("BILLING_PROVIDER_REFUSED", "The payment provider refused the request. Check the subscription in the billing portal.");

    /// <summary>
    /// True when the provider never acted: it could not be reached, or it refused the request as it stood.
    /// A timeout or a server error may have acted, so it is not.
    /// </summary>
    public static bool DidNothing(HttpRequestException ex) =>
        ex.HttpRequestError == HttpRequestError.ConnectionError
        || ex.StatusCode is { } status && (int)status is >= 400 and < 500 && status is not (HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity);

    /// <summary>
    /// A refusal of the request itself is a 409, which trying again would not change. Anything else (no
    /// answer, a timeout, a server error, too many requests, or credentials the provider does not accept,
    /// which is this deployment's to fix) is a 503 to try again.
    /// </summary>
    public static Error ToError(HttpRequestException ex) =>
        ex.StatusCode is { } status
        && (int)status is >= 400 and < 500
        && status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
            ? Refused
            : Unavailable;
}
