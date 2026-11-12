namespace Wbskt.Auth.Host.Telemetry;

/// <summary>The <c>outcome</c> values of <c>wbskt_auth_mail_total</c>, which alerts.yml and the dashboard name too.</summary>
public static class MailOutcome
{
    /// <summary>The relay took the message.</summary>
    public const string Sent = "sent";

    /// <summary>One attempt failed and the message is tried again. Counts attempts, not messages.</summary>
    public const string Retried = "retried";

    /// <summary>Given up on after the retries: the relay is refusing or not answering.</summary>
    public const string Dropped = "dropped";

    /// <summary>Refused before it was queued because the queue was full: the relay is not keeping up.</summary>
    public const string QueueFull = "queue_full";

    /// <summary>The recipient address is malformed, so it is never tried again.</summary>
    public const string InvalidAddress = "invalid_address";

    /// <summary>The relay refused this recipient for good (a 5xx reply), so it is never tried again.</summary>
    public const string Rejected = "rejected";

    /// <summary>Not sent because no relay is configured.</summary>
    public const string NotConfigured = "not_configured";
}
