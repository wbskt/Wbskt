using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Wbskt.Infrastructure.Email;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>What a failed send says about itself without the recipient's address.</summary>
internal static class MailFailure
{
    /// <summary>
    /// The exception's type, with the relay's status and MailKit's reading of it for an SMTP error,
    /// then the type of each exception inside it. Never a relay's reply: it usually repeats the address
    /// (<c>5.1.1 &lt;user@example.com&gt;: Recipient address rejected</c>) or the user name, and what this
    /// returns is logged and stored.
    /// </summary>
    /// <remarks>
    /// A relay that cannot be reached at all (refused, DNS) is a <see cref="SocketException"/>, possibly
    /// inside another exception, so inner exceptions are named too: by type, and by socket error for a
    /// <see cref="SocketException"/>, neither of which holds an address.
    /// </remarks>
    public static string Describe(Exception ex)
    {
        string described = ex switch
        {
            SmtpCommandException smtp => $"{nameof(SmtpCommandException)} ({(int)smtp.StatusCode} {smtp.StatusCode}, {smtp.ErrorCode})",
            AuthenticationException => $"{nameof(AuthenticationException)}; the relay refused the login (check Auth:Email UserName and Password)",
            SocketException socket => $"{nameof(SocketException)} ({socket.SocketErrorCode})",
            _ => $"{ex.GetType().Name}: {ex.Message}"
        };

        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            described += inner is SocketException socket
                ? $" <- {nameof(SocketException)} ({socket.SocketErrorCode})"
                : $" <- {inner.GetType().Name}";
        }

        if (ex is SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted })
        {
            described += "; the relay refused the sender (check Auth:Email FromAddress, and that the relay lets this login send as it)";
        }

        return described;
    }

    /// <summary>Whether the relay refused this recipient for good, so trying again cannot help.</summary>
    public static bool IsPermanentRecipientRefusal(Exception ex) =>
        ex is SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } recipient && (int)recipient.StatusCode >= 500;

    /// <summary>Whether <paramref name="ex"/> is about the recipient's address being malformed, not the sender's.</summary>
    public static bool IsMalformedRecipient(Exception ex, string to) =>
        ex is FormatException && !SmtpTransport.TryParseRecipients(to, out _);
}
