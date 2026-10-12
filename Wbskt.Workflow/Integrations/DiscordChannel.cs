using System.Net.Http.Json;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// A Discord channel webhook. The URL is the credential and may only point at Discord's own hosts
/// (checked when it is saved), so no address guard is needed.
/// </summary>
internal sealed class DiscordChannel(IHttpClientFactory httpClientFactory) : IIntegrationChannel
{
    private const string ServiceName = "Discord";

    public IntegrationType Type => IntegrationType.Discord;

    public async Task<ChannelResult> TestAsync(ResolvedIntegration integration, IntegrationTestOptions? options, CancellationToken ct)
    {
        // A GET describes the webhook without posting anything to the channel.
        using var request = new HttpRequestMessage(HttpMethod.Get, integration.Secrets.WebhookUrl);
        var (response, failure) = await HttpChannelSupport.SendAsync(Client(), request, null, ServiceName, ct);
        if (failure is not null)
        {
            return Scrubbed(failure.ForTest(), integration);
        }

        using (response)
        {
            if (response!.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Unauthorized)
            {
                return ChannelResult.Fail("INTEGRATION_AUTH_FAILED", "Discord does not know this webhook; it may have been deleted.", false, (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Scrubbed(HttpChannelSupport.FromStatus(response.StatusCode, ServiceName).ForTest(), integration);
            }

            JsonElement? metadata = null;
            try
            {
                var hook = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                metadata = JsonSerializer.SerializeToElement(new
                {
                    webhookName = hook.TryGetProperty("name", out var n) ? n.GetString() : null,
                    channelId = hook.TryGetProperty("channel_id", out var c) ? c.GetString() : null
                });
            }
            catch (JsonException)
            {
            }

            return ChannelResult.Success(200, metadata);
        }
    }

    public async Task<ChannelResult> SendAsync(ResolvedIntegration integration, string message, string? username, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, integration.Secrets.WebhookUrl)
        {
            // No mentions are parsed: a message built from device data must not be able to ping @everyone.
            Content = JsonContent.Create(new
            {
                content = message,
                username = string.IsNullOrWhiteSpace(username) ? null : username,
                allowed_mentions = new { parse = Array.Empty<string>() }
            }, options: new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })
        };

        var (response, failure) = await HttpChannelSupport.SendAsync(Client(), request, null, ServiceName, ct);
        if (failure is not null)
        {
            return Scrubbed(failure.ForStep("DISCORD"), integration);
        }

        using (response)
        {
            return response!.IsSuccessStatusCode
                ? ChannelResult.Success((int)response.StatusCode)
                : Scrubbed(HttpChannelSupport.FromStatus(response.StatusCode, ServiceName).ForStep("DISCORD"), integration);
        }
    }

    private HttpClient Client() => httpClientFactory.CreateClient(HttpChannelSupport.ClientName);

    private static ChannelResult Scrubbed(ChannelResult result, ResolvedIntegration integration) =>
        result with { Message = IntegrationErrors.Scrub(result.Message, integration.Secrets) };
}
