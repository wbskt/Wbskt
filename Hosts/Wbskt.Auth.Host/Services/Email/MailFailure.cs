using System.Net.Mail;
using System.Net.Sockets;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>What a failed send says about itself without the recipient's address.</summary>
internal static class MailFailure
{
    /// <summary>
    /// The exception's type, with the relay's status code for an SMTP error, then the type of each
    /// exception inside it. Never an SMTP error's message: a relay's reply usually repeats the address
    /// (<c>5.1.1 &lt;user@example.com&gt;: Recipient address rejected</c>), and what this returns is
    /// logged and stored.
    /// </summary>
    /// <remarks>
    /// A relay that cannot be reached at all (refused, TLS, DNS) is an <see cref="SmtpException"/> with
    /// <c>GeneralFailure</c>, and the cause is only in its inner exceptions, so they are named too: by
    /// type, and by socket error for a <see cref="SocketException"/>, neither of which holds an address.
    /// </remarks>
    public static string Describe(Exception ex)
    {
        string described = ex is SmtpException smtp
            ? $"{ex.GetType().Name} ({(int)smtp.StatusCode} {smtp.StatusCode})"
            : $"{ex.GetType().Name}: {ex.Message}";
        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            described += inner is SocketException socket
                ? $" <- {nameof(SocketException)} ({socket.SocketErrorCode})"
                : $" <- {inner.GetType().Name}";
        }

        return described;
    }

    /// <summary>Whether the relay refused this recipient for good, so trying again cannot help.</summary>
    public static bool IsPermanentRecipientRefusal(Exception ex) =>
        ex is SmtpFailedRecipientException recipient && (int)recipient.StatusCode >= 500;

    /// <summary>Whether <paramref name="ex"/> is about the recipient's address being malformed, not the sender's.</summary>
    public static bool IsMalformedRecipient(Exception ex, string to) =>
        ex is FormatException && !MailAddress.TryCreate(to, out _);
}
