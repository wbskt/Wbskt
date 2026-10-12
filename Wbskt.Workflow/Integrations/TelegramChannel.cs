using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;
using static Wbskt.Workflow.Integrations.IntegrationErrors;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// The Telegram Bot API. The token only ever goes to the configured API base (api.telegram.org in
/// production), never to an address a workflow author chose, so no address guard is needed.
/// </summary>
internal sealed class TelegramChannel(IHttpClientFactory httpClientFactory, IOptions<IntegrationOptions> options) : IIntegrationChannel
{
    private const string ServiceName = "Telegram";
    private const string TestMessage = "Test message from Wbskt: this bot can post here.";

    public IntegrationType Type => IntegrationType.Telegram;

    public async Task<ChannelResult> TestAsync(ResolvedIntegration integration, IntegrationTestOptions? testOptions, CancellationToken ct)
    {
        string token = integration.Secrets.BotToken!;
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(token, "getMe"));
        var (response, failure) = await HttpChannelSupport.SendAsync(Client(), request, null, ServiceName, ct);
        if (failure is not null)
        {
            return Scrubbed(failure.ForTest(), integration);
        }

        using (response)
        {
            if (!response!.IsSuccessStatusCode)
            {
                // getMe answers 401 for a revoked token and 404 for one that never existed.
                return response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? ChannelResult.Fail("INTEGRATION_AUTH_FAILED", "Telegram does not know this bot token.", false, 404)
                    : Scrubbed(HttpChannelSupport.FromStatus(response.StatusCode, ServiceName).ForTest(), integration);
            }

            JsonElement? metadata = null;
            try
            {
                var me = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (me.TryGetProperty("result", out var result))
                {
                    metadata = JsonSerializer.SerializeToElement(new
                    {
                        botUsername = result.TryGetProperty("username", out var u) ? u.GetString() : null,
                        botName = result.TryGetProperty("first_name", out var n) ? n.GetString() : null
                    });
                }
            }
            catch (JsonException)
            {
                // The token works; what Telegram said about the bot is a nicety.
            }

            if (!string.IsNullOrWhiteSpace(testOptions?.ChatId))
            {
                var sent = await SendMessageAsync(integration, testOptions.ChatId.Trim(), TestMessage, ct);
                if (!sent.Ok)
                {
                    return sent with { Code = sent.Code!.Replace("TELEGRAM_", "INTEGRATION_", StringComparison.Ordinal).Replace("NETWORK_ERROR", "UNREACHABLE", StringComparison.Ordinal) };
                }
            }

            return ChannelResult.Success(200, metadata);
        }
    }

    public async Task<ChannelResult> SendMessageAsync(ResolvedIntegration integration, string chatId, string text, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(integration.Secrets.BotToken!, "sendMessage"))
        {
            Content = JsonContent.Create(new { chat_id = chatId, text })
        };

        var (response, failure) = await HttpChannelSupport.SendAsync(Client(), request, null, ServiceName, ct);
        if (failure is not null)
        {
            return Scrubbed(failure.ForStep("TELEGRAM"), integration);
        }

        using (response)
        {
            if (response!.IsSuccessStatusCode)
            {
                return ChannelResult.Success((int)response.StatusCode);
            }

            // Telegram's description ("Bad Request: chat not found") is the useful part, and it is its
            // own text rather than an echo of the request; scrubbed all the same.
            string? description = null;
            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                description = body.TryGetProperty("description", out var d) ? d.GetString() : null;
            }
            catch (JsonException)
            {
            }

            return Scrubbed(HttpChannelSupport.FromStatus(response.StatusCode, ServiceName, description).ForStep("TELEGRAM"), integration);
        }
    }

    private HttpClient Client() => httpClientFactory.CreateClient(HttpChannelSupport.ClientName);

    private string Url(string token, string method) => $"{options.Value.TelegramApiBaseUrl.TrimEnd('/')}/bot{token}/{method}";
}
