using System.Net;
using System.Net.Sockets;
using MailKit.Security;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Email;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>A relay that stops answering costs one send its timeout, not the mail drain behind it.</summary>
public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task A_relay_that_never_answers_fails_the_send_with_a_timeout()
    {
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        var accepting = relay.AcceptTcpClientAsync();
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)relay.LocalEndpoint).Port,
            UseStartTls = false,
            FromAddress = "noreply@example.test",
            SendTimeout = TimeSpan.FromMilliseconds(300)
        }));

        var send = sender.SendAsync("someone@example.test", "subject", "body", EmailBodyFormat.PlainText, CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
        (await accepting).Dispose();
    }

    [Fact]
    public async Task Stopping_the_host_cancels_rather_than_times_out()
    {
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        var accepting = relay.AcceptTcpClientAsync();
        using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)relay.LocalEndpoint).Port,
            UseStartTls = false,
            FromAddress = "noreply@example.test",
            SendTimeout = TimeSpan.FromMinutes(1)
        }));

        var send = sender.SendAsync("someone@example.test", "subject", "body", EmailBodyFormat.PlainText, stopping.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
        (await accepting).Dispose();
    }

    [Theory]
    [InlineData(465, true, SecureSocketOptions.SslOnConnect)]
    [InlineData(465, false, SecureSocketOptions.SslOnConnect)]
    [InlineData(587, true, SecureSocketOptions.StartTls)]
    [InlineData(25, false, SecureSocketOptions.None)]
    public void Port_465_means_tls_from_the_start_and_other_ports_starttls(int port, bool useStartTls, SecureSocketOptions expected)
    {
        Assert.Equal(expected, SmtpTransport.SocketOptions(SmtpTransport.ImpliesImplicitTls(port), useStartTls, SecureSocketOptions.None));
    }

    [Theory]
    [InlineData("someone@example.test", true)]
    [InlineData("a@example.test, b@example.test", true)]
    [InlineData("not-an-address", false)]
    [InlineData("", false)]
    public void A_recipient_is_one_or_more_addresses(string to, bool valid)
    {
        Assert.Equal(valid, SmtpTransport.TryParseRecipients(to, out _));
    }

    [Fact]
    public async Task A_malformed_recipient_fails_before_dialling()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions { Host = "relay.invalid", FromAddress = "noreply@example.test" }));

        await Assert.ThrowsAsync<FormatException>(() => sender.SendAsync("not-an-address", "subject", "body", EmailBodyFormat.PlainText, CancellationToken.None));
    }
}
