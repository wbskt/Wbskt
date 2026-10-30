using System.Net.Http.Json;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Engine.Contracts;
using static Wbskt.Workflow.Integrations.IntegrationErrors;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// A Discord channel webhook. The URL is the credential and may only point at Discord's own hosts
/// (checked when it is saved), so no address guard is needed.
/// </summary>
internal sealed class DiscordChannel(IHttpClientFactory httpClientFactory, TimeProvider? timeProvider = null) : IIntegrationChannel
{
    private const string ServiceName = "Discord";

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    private static readonly JsonSerializerOptions MessageJsonOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

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

    /// <summary>
    /// Posts the step's message and asks Discord to wait for it to be saved, so the result carries the
    /// message's <c>messageId</c> and <c>channelId</c>.
    /// </summary>
    public async Task<ChannelResult> SendAsync(ResolvedIntegration integration, DiscordConfig config, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SendUri(integration.Secrets.WebhookUrl!, config.ThreadId))
        {
            // No mentions are parsed: a message built from device data must not be able to ping @everyone.
            Content = JsonContent.Create(new
            {
                content = NullIfBlank(config.Message),
                username = NullIfBlank(config.Username),
                avatar_url = NullIfBlank(config.AvatarUrl),
                embeds = config.Embeds is { Count: > 0 } embeds ? embeds.Select(Embed).ToArray() : null,
                allowed_mentions = new { parse = Array.Empty<string>() }
            }, options: MessageJsonOptions)
        };

        var (response, failure) = await HttpChannelSupport.SendAsync(Client(), request, null, ServiceName, ct);
        if (failure is not null)
        {
            return Scrubbed(failure.ForStep("DISCORD"), integration);
        }

        using (response)
        {
            if (!response!.IsSuccessStatusCode)
            {
                return Scrubbed(HttpChannelSupport.FromStatus(response.StatusCode, ServiceName).ForStep("DISCORD"), integration);
            }

            JsonElement? sent = null;
            try
            {
                var message = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                sent = JsonSerializer.SerializeToElement(new
                {
                    messageId = message.TryGetProperty("id", out var id) ? id.GetString() : null,
                    channelId = message.TryGetProperty("channel_id", out var c) ? c.GetString() : null
                });
            }
            catch (JsonException)
            {
                // Posted all the same; only the ids are missing.
            }

            return ChannelResult.Success((int)response.StatusCode, sent);
        }
    }

    private static Uri SendUri(string webhookUrl, string? threadId)
    {
        var builder = new UriBuilder(webhookUrl);
        string query = builder.Query.TrimStart('?');
        query += (query.Length > 0 ? "&" : string.Empty) + "wait=true";
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            query += "&thread_id=" + Uri.EscapeDataString(threadId);
        }

        builder.Query = query;
        return builder.Uri;
    }

    private object Embed(DiscordEmbed embed) => new
    {
        title = NullIfBlank(embed.Title),
        url = NullIfBlank(embed.Url),
        description = NullIfBlank(embed.Description),
        color = embed.Color is { Length: 7 } hex && int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int rgb) ? rgb : (int?)null,
        fields = embed.Fields is { Count: > 0 } fields
            ? fields.Select(f => new { name = f.Name, value = f.Value, inline = f.Inline }).ToArray()
            : null,
        footer = string.IsNullOrWhiteSpace(embed.Footer) ? null : new { text = embed.Footer },
        image = string.IsNullOrWhiteSpace(embed.ImageUrl) ? null : new { url = embed.ImageUrl },
        thumbnail = string.IsNullOrWhiteSpace(embed.ThumbnailUrl) ? null : new { url = embed.ThumbnailUrl },
        timestamp = embed.Timestamp ? _clock.GetUtcNow().ToString("O") : null
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private HttpClient Client() => httpClientFactory.CreateClient(HttpChannelSupport.ClientName);
}
