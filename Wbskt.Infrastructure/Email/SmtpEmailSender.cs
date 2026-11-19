using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Wbskt.Infrastructure.Email;

/// <summary>
/// Sends through a plain SMTP relay configured on the host, over MailKit as the workflow email step
/// does, so a relay that needs TLS from the start (port 465) works as well as one using STARTTLS.
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
        var message = CreateMessage(to, subject);
        message.Body = new TextPart(format == EmailBodyFormat.Html ? "html" : "plain") { Text = body };

        await DeliverAsync(message, ct);
    }

    public async Task SendMultipartAsync(string to, string subject, string textBody, string htmlBody, CancellationToken ct)
    {
        var message = CreateMessage(to, subject);

        // multipart/alternative with the text first and the HTML after it; clients show the last part
        // they can render, so HTML wins wherever it is allowed.
        message.Body = new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody();

        await DeliverAsync(message, ct);
    }

    private MimeMessage CreateMessage(string to, string subject)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("No SMTP host or from-address is configured for this host's email section.");
        }

        if (!SmtpTransport.TryParseRecipients(to, out var recipients))
        {
            // Never the address itself: this message reaches the logs.
            throw new FormatException("The recipient is not an email address.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromDisplayName ?? string.Empty, _options.FromAddress!));
        message.To.AddRange(recipients);
        message.Subject = subject;
        return message;
    }

    private async Task DeliverAsync(MimeMessage message, CancellationToken ct)
    {
        // Off only against a local relay, which then gets no TLS at all, as before.
        var socketOptions = SmtpTransport.SocketOptions(
            SmtpTransport.ImpliesImplicitTls(_options.Port), _options.UseStartTls, SecureSocketOptions.None);

        // The client's own timeout bounds each step; the token bounds the whole send, so a relay that
        // answers slowly at every step still costs one timeout and not one per step.
        using var client = new SmtpClient { Timeout = (int)_options.SendTimeout.TotalMilliseconds };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.SendTimeout);
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, socketOptions, timeout.Token);

            // Anonymous relays are legitimate on an internal network, so credentials are optional.
            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                await client.AuthenticateAsync(_options.UserName, _options.Password ?? string.Empty, timeout.Token);
            }

            await client.SendAsync(message, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The SMTP relay {_options.Host}:{_options.Port} did not take the message within {_options.SendTimeout}.", ex);
        }
    }
}
