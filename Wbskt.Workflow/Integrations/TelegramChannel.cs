using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
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

    private static readonly JsonSerializerOptions MessageJsonOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

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
                var sent = await SendAsync(integration, new TelegramConfig { ChatId = testOptions.ChatId.Trim(), Message = TestMessage }, ct);
                if (!sent.Ok)
                {
                    return sent with { Code = sent.Code!.Replace("TELEGRAM_", "INTEGRATION_", StringComparison.Ordinal).Replace("NETWORK_ERROR", "UNREACHABLE", StringComparison.Ordinal) };
                }
            }

            return ChannelResult.Success(200, metadata);
        }
    }

    /// <summary>
    /// Sends the step's message, and returns the sent message's <c>messageId</c> and <c>chatId</c> as
    /// Telegram reports them (a <c>@channelusername</c> comes back as the channel's numeric id).
    /// </summary>
    public async Task<ChannelResult> SendAsync(ResolvedIntegration integration, TelegramConfig config, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(integration.Secrets.BotToken!, "sendMessage"))
        {
            Content = JsonContent.Create(new
            {
                chat_id = config.ChatId,
                text = config.Message,
                parse_mode = string.IsNullOrWhiteSpace(config.ParseMode) ? null : config.ParseMode,
                message_thread_id = int.TryParse(config.MessageThreadId, out int threadId) ? threadId : (int?)null,
                disable_notification = config.Silent ? true : (bool?)null,
                link_preview_options = config.DisableLinkPreview ? new { is_disabled = true } : null
            }, options: MessageJsonOptions)
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
                JsonElement? sent = null;
                try
                {
                    var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                    if (body.TryGetProperty("result", out var message))
                    {
                        sent = JsonSerializer.SerializeToElement(new
                        {
                            messageId = message.TryGetProperty("message_id", out var id) && id.TryGetInt64(out long m) ? m : (long?)null,
                            chatId = message.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var c) && c.TryGetInt64(out long cid) ? cid : (long?)null
                        });
                    }
                }
                catch (JsonException)
                {
                    // Sent all the same; only the ids are missing.
                }

                return ChannelResult.Success((int)response.StatusCode, sent);
            }

            // Telegram's description ("Bad Request: chat not found") is the useful part, and it is its
            // own text rather than an echo of the request; scrubbed all the same.
            string? description = await DescriptionAsync(response, ct);

            // A bad token is 401 (or 404); a 403 on sendMessage is the chat refusing the bot: blocked
            // by the user, kicked from the group, or not allowed to post in the channel.
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                string detail = string.IsNullOrWhiteSpace(description) ? string.Empty : $": {description}";
                return Scrubbed(ChannelResult.Fail("TELEGRAM_CHAT_FORBIDDEN", $"Telegram will not let the bot post in this chat (403){detail}.", false, 403), integration);
            }

            return Scrubbed(HttpChannelSupport.FromStatus(response.StatusCode, ServiceName, description).ForStep("TELEGRAM"), integration);
        }
    }

    /// <summary>
    /// The chats the bot has heard from lately, from <c>getUpdates</c>, most recent first. Telegram keeps
    /// updates for 24 hours, and answers 409 while the bot has a webhook set. No offset is sent, so
    /// nothing is marked as read and the updates stay for whoever else polls the bot.
    /// </summary>
    public async Task<TelegramChatsResult> GetChatsAsync(ResolvedIntegration integration, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(integration.Secrets.BotToken!, "getUpdates"));
        var (response, failure) = await HttpChannelSupport.SendAsync(Client(), request, null, ServiceName, ct);
        if (failure is not null)
        {
            return new TelegramChatsResult(Scrubbed(failure.ForTest(), integration), []);
        }

        using (response)
        {
            if (!response!.IsSuccessStatusCode)
            {
                string? description = await DescriptionAsync(response, ct);
                var refused = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Conflict => ChannelResult.Fail(
                        "INTEGRATION_WEBHOOK_ACTIVE",
                        "This bot has a webhook set, so Telegram will not list its recent chats. Type the chat id instead, or remove the webhook.",
                        false,
                        409),
                    System.Net.HttpStatusCode.NotFound => ChannelResult.Fail("INTEGRATION_AUTH_FAILED", "Telegram does not know this bot token.", false, 404),
                    _ => HttpChannelSupport.FromStatus(response.StatusCode, ServiceName, description).ForTest()
                };
                return new TelegramChatsResult(Scrubbed(refused, integration), []);
            }

            var chats = new List<TelegramChat>();
            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (body.TryGetProperty("result", out var updates) && updates.ValueKind == JsonValueKind.Array)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var update in updates.EnumerateArray().Reverse())
                    {
                        if (ChatOf(update) is { } chat && seen.Add(chat.Id))
                        {
                            chats.Add(chat);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return new TelegramChatsResult(ChannelResult.Fail("INTEGRATION_HTTP_ERROR", "Telegram's answer could not be read.", true, (int)response.StatusCode), []);
            }

            return new TelegramChatsResult(ChannelResult.Success((int)response.StatusCode), chats);
        }
    }

    /// <summary>
    /// The chat an update happened in. Every update type that has one carries it as <c>chat</c> on its
    /// payload (a message, a member change, a join request), or on the payload's <c>message</c> for a
    /// button press.
    /// </summary>
    private static TelegramChat? ChatOf(JsonElement update)
    {
        if (update.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in update.EnumerateObject())
        {
            var payload = property.Value;
            if (payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!payload.TryGetProperty("chat", out var chat) && !(payload.TryGetProperty("message", out var inner) && inner.ValueKind == JsonValueKind.Object && inner.TryGetProperty("chat", out chat)))
            {
                continue;
            }

            if (chat.ValueKind != JsonValueKind.Object || !chat.TryGetProperty("id", out var id) || !id.TryGetInt64(out long chatId))
            {
                continue;
            }

            // A group or channel has a title; a private chat has the person's name instead.
            string person = string.Join(' ', new[] { Text(chat, "first_name"), Text(chat, "last_name") }.Where(n => !string.IsNullOrWhiteSpace(n)));
            string? title = Text(chat, "title") ?? (person.Length > 0 ? person : null);
            return new TelegramChat(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture), Text(chat, "type") ?? "unknown", title, Text(chat, "username"));
        }

        return null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<string?> DescriptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return body.TryGetProperty("description", out var d) ? d.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private HttpClient Client() => httpClientFactory.CreateClient(HttpChannelSupport.ClientName);

    private string Url(string token, string method) => $"{options.Value.TelegramApiBaseUrl.TrimEnd('/')}/bot{token}/{method}";
}
