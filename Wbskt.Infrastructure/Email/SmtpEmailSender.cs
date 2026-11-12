using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Options;

namespace Wbskt.Infrastructure.Email;

/// <summary>
/// Sends through a plain SMTP relay configured on the host.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions>? options = null)
    {
        _options = options?.Value ?? new EmailOptions();
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(string to, string subject, string body, EmailBodyFormat format, CancellationToken ct)
    {
        using var message = CreateMessage(to, subject);
        message.Body = body;
        message.IsBodyHtml = format == EmailBodyFormat.Html;

        await DeliverAsync(message, ct);
    }

    public async Task SendMultipartAsync(string to, string subject, string textBody, string htmlBody, CancellationToken ct)
    {
        using var message = CreateMessage(to, subject);

        // The body goes first in the multipart/alternative and the HTML view after it; clients show
        // the last part they can render, so HTML wins wherever it is allowed.
        message.Body = textBody;
        message.IsBodyHtml = false;
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));

        await DeliverAsync(message, ct);
    }

    private MailMessage CreateMessage(string to, string subject)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("No SMTP host or from-address is configured for this host's email section.");
        }

        var message = new MailMessage
        {
            From = string.IsNullOrWhiteSpace(_options.FromDisplayName)
                ? new MailAddress(_options.FromAddress!)
                : new MailAddress(_options.FromAddress!, _options.FromDisplayName),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };

        // Multiple recipients are ordinary; MailAddressCollection parses a comma-separated list.
        message.To.Add(to);
        return message;
    }

    private async Task DeliverAsync(MailMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseStartTls
        };

        // Anonymous relays are legitimate on an internal network, so credentials are optional; sending
        // the machine's own Windows credentials to an external relay, which is what UseDefaultCredentials
        // would do, is not.
        client.UseDefaultCredentials = false;
        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        // SmtpClient.Timeout covers only the synchronous Send, so the bound is a token.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.SendTimeout);
        try
        {
            await client.SendMailAsync(message, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The SMTP relay {_options.Host}:{_options.Port} did not take the message within {_options.SendTimeout}.", ex);
        }
    }
}
