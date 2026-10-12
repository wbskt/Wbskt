namespace Wbskt.Workflow.Integrations;

/// <summary>How a send or a test went wrong, before it is named for a step kind or for a test.</summary>
internal enum ChannelFailureKind
{
    /// <summary>The service refused the credential.</summary>
    AuthFailed,

    /// <summary>The service could not be reached at all.</summary>
    NetworkError,

    /// <summary>The service answered with an HTTP error.</summary>
    HttpError,

    /// <summary>The target resolves to an address the engine may not call.</summary>
    BlockedTarget,

    /// <summary>The TLS handshake failed.</summary>
    TlsFailed,

    /// <summary>The mail relay refused a recipient.</summary>
    RecipientRefused,

    /// <summary>An address the step gave is malformed.</summary>
    AddressInvalid,

    /// <summary>Anything else the service refused.</summary>
    SendError
}

internal sealed record ChannelFailure(ChannelFailureKind Kind, string Message, bool Retryable, int? Status = null)
{
    private string Suffix => Kind switch
    {
        ChannelFailureKind.AuthFailed => "AUTH_FAILED",
        ChannelFailureKind.NetworkError => "NETWORK_ERROR",
        ChannelFailureKind.HttpError => "HTTP_ERROR",
        ChannelFailureKind.BlockedTarget => "BLOCKED_TARGET",
        ChannelFailureKind.TlsFailed => "TLS_FAILED",
        ChannelFailureKind.RecipientRefused => "RECIPIENT_REFUSED",
        ChannelFailureKind.AddressInvalid => "ADDRESS_INVALID",
        _ => "SEND_ERROR"
    };

    /// <summary>As a step's failure: <c>TELEGRAM_HTTP_ERROR</c>, <c>EMAIL_SEND_ERROR</c> and so on.</summary>
    public ChannelResult ForStep(string prefix) => ChannelResult.Fail($"{prefix}_{Suffix}", Message, Retryable, Status);

    /// <summary>As a test's failure, the same for every type: <c>INTEGRATION_AUTH_FAILED</c>, <c>INTEGRATION_UNREACHABLE</c>.</summary>
    public ChannelResult ForTest() => ChannelResult.Fail(
        Kind == ChannelFailureKind.NetworkError ? "INTEGRATION_UNREACHABLE" : $"INTEGRATION_{Suffix}",
        Message,
        Retryable,
        Status);
}
