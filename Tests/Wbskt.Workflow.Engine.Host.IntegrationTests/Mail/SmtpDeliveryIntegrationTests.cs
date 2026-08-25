using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Email;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Mail;

/// <summary>
/// Actual delivery: <see cref="SmtpEmailSender"/> talking SMTP to a real relay, and the message read
/// back out of it.
///
/// Everything else in the suite stops at "the right message was handed to the queue". That leaves the
/// entire SMTP boundary — the port, the TLS mode, the credentials, the envelope sender the relay is
/// willing to accept, whether the body arrives as HTML or as literal markup — verified nowhere but
/// production. Since sign-in requires a confirmed address, "mail does not actually leave" and "nobody
/// can sign up" are the same outage, so it is worth one container to find out.
///
/// <para>
/// Needs a mail sink speaking SMTP with an HTTP API to read from. CI runs
/// <c>axllent/mailpit</c> alongside SQL Server. Locally:
/// <code>docker run -d -p 1025:1025 -p 8025:8025 axllent/mailpit</code>
/// Override with WBSKT_SMTP_TEST_HOST / _PORT / _API. Skips when the sink is unreachable.
/// </para>
/// </summary>
public sealed class SmtpDeliveryIntegrationTests
{
    private static string SinkHost => Environment.GetEnvironmentVariable("WBSKT_SMTP_TEST_HOST") ?? "localhost";

    private static int SinkSmtpPort =>
        int.TryParse(Environment.GetEnvironmentVariable("WBSKT_SMTP_TEST_PORT"), out int port) ? port : 1025;

    private static string SinkApi =>
        (Environment.GetEnvironmentVariable("WBSKT_SMTP_TEST_API") ?? "http://localhost:8025").TrimEnd('/');

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    [SkippableFact]
    public async Task A_message_reaches_the_relay_and_arrives_as_html()
    {
        Skip.IfNot(await SinkIsReachableAsync(), "No SMTP sink reachable — skipping.");

        string marker = $"probe-{Guid.NewGuid():N}";
        var sender = NewSender();

        Assert.True(sender.IsConfigured);

        await sender.SendAsync(
            $"{marker}@example.test",
            $"Confirm your address {marker}",
            $"<p>Hello <b>{marker}</b></p>",
            EmailBodyFormat.Html,
            CancellationToken.None);

        JsonElement message = await WaitForMessageAsync(marker);

        Assert.Equal($"Confirm your address {marker}", message.GetProperty("Subject").GetString());

        // HTML, not a literal <p> in the recipient's client. This is the assertion that proves the
        // per-send body format actually reaches the wire rather than just the sender's API.
        Assert.Contains($"<b>{marker}</b>", message.GetProperty("HTML").GetString() ?? string.Empty);
    }

    /// <summary>
    /// The workflow email node's body is written by a workspace member and sanitised nowhere, so it
    /// must arrive as text a client renders literally. Asserting it at the wire, because this is the
    /// one property that a mistake in the sender would quietly invert.
    /// </summary>
    [SkippableFact]
    public async Task A_plain_text_message_arrives_without_being_treated_as_markup()
    {
        Skip.IfNot(await SinkIsReachableAsync(), "No SMTP sink reachable — skipping.");

        string marker = $"probe-{Guid.NewGuid():N}";
        var sender = NewSender();

        await sender.SendAsync(
            $"{marker}@example.test",
            $"Plain {marker}",
            $"<script>alert('{marker}')</script>",
            EmailBodyFormat.PlainText,
            CancellationToken.None);

        JsonElement message = await WaitForMessageAsync(marker);

        Assert.Contains(marker, message.GetProperty("Text").GetString() ?? string.Empty);
        Assert.True(string.IsNullOrEmpty(message.GetProperty("HTML").GetString()));
    }

    [SkippableFact]
    public async Task The_configured_envelope_sender_is_what_the_relay_sees()
    {
        Skip.IfNot(await SinkIsReachableAsync(), "No SMTP sink reachable — skipping.");

        string marker = $"probe-{Guid.NewGuid():N}";
        var sender = NewSender();

        await sender.SendAsync($"{marker}@example.test", $"From check {marker}", "body", EmailBodyFormat.PlainText, CancellationToken.None);

        JsonElement message = await WaitForMessageAsync(marker);

        Assert.Equal("no-reply@wbskt.test", message.GetProperty("From").GetProperty("Address").GetString());
        Assert.Equal("Wbskt Test", message.GetProperty("From").GetProperty("Name").GetString());
    }

    /// <summary>
    /// A host with no relay configured must say so rather than failing somewhere inside the SMTP
    /// stack with a message about a null hostname. Nothing is sent, and nothing tries.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_sender_explains_itself_instead_of_dialling()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions()));

        Assert.False(sender.IsConfigured);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync("someone@example.test", "s", "b", EmailBodyFormat.PlainText, CancellationToken.None));

        Assert.Contains("SMTP", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- helpers

    private static SmtpEmailSender NewSender() =>
        new(Options.Create(new EmailOptions
        {
            Host = SinkHost,
            Port = SinkSmtpPort,

            // The sink speaks plain SMTP on this port. That makes this a test of the plumbing rather
            // than of TLS negotiation; a relay that needs STARTTLS is still only exercised in a real
            // deployment.
            UseStartTls = false,
            FromAddress = "no-reply@wbskt.test",
            FromDisplayName = "Wbskt Test"
        }));

    private static async Task<bool> SinkIsReachableAsync()
    {
        try
        {
            var response = await Http.GetAsync($"{SinkApi}/api/v1/messages?limit=1");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Polls the sink for a message whose subject carries <paramref name="marker"/>. Delivery is
    /// asynchronous on the sink's side, so this waits rather than reading once and failing.
    /// </summary>
    private static async Task<JsonElement> WaitForMessageAsync(string marker)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var listing = await Http.GetFromJsonAsync<JsonElement>($"{SinkApi}/api/v1/search?query={Uri.EscapeDataString(marker)}");

            if (listing.TryGetProperty("messages", out JsonElement messages) && messages.GetArrayLength() > 0)
            {
                string id = messages[0].GetProperty("ID").GetString()!;
                return await Http.GetFromJsonAsync<JsonElement>($"{SinkApi}/api/v1/message/{id}");
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"No message matching '{marker}' arrived at the sink within 10 seconds.");
    }
}
