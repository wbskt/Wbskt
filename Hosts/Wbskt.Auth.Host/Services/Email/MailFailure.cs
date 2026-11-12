using System.Net.Mail;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>What a failed send says about itself without the recipient's address.</summary>
internal static class MailFailure
{
    /// <summary>
    /// The exception's type, with the relay's status code for an SMTP error. Never its message: a
    /// relay's reply usually repeats the address (<c>5.1.1 &lt;user@example.com&gt;: Recipient address
    /// rejected</c>), and what this returns is logged and stored.
    /// </summary>
    public static string Describe(Exception ex) => ex is SmtpException smtp
        ? $"{ex.GetType().Name} ({(int)smtp.StatusCode} {smtp.StatusCode})"
        : $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>Whether the relay refused this recipient for good, so trying again cannot help.</summary>
    public static bool IsPermanentRecipientRefusal(Exception ex) =>
        ex is SmtpFailedRecipientException recipient && (int)recipient.StatusCode >= 500;

    /// <summary>Whether <paramref name="ex"/> is about the recipient's address being malformed, not the sender's.</summary>
    public static bool IsMalformedRecipient(Exception ex, string to) =>
        ex is FormatException && !MailAddress.TryCreate(to, out _);
}
