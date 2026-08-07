using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Posts a message through the Telegram Bot API.
/// </summary>
/// <remarks>
/// The bot token comes from host configuration, never from the node: a definition is stored, versioned
/// and readable by the whole workspace, so a token written into one leaks to every member and is frozen
/// into every published version.
/// </remarks>
internal sealed class TelegramNodeExecutor(IHttpClientFactory httpClientFactory, IOptions<TelegramOptions>? options = null) : INodeExecutor
{
    private readonly TelegramOptions _options = options?.Value ?? new TelegramOptions();

    public string Kind => NodeKind.ActionTelegram;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (TelegramNotificationNode)ctx.Node;
        TelegramConfig config = node.Config
            ?? throw new InvalidOperationException($"Telegram node {node.NodeId} is missing config.");

        if (!_options.IsConfigured)
        {
            return new NodeExecutionResult.Fail(
                "TELEGRAM_NOT_CONFIGURED",
                "This engine has no Telegram bot token configured (WorkflowEngine:Telegram), so telegram nodes cannot send.",
                false,
                null);
        }

        HttpClient client = httpClientFactory.CreateClient("workflow-telegram");

        try
        {
            // No address guard here, unlike action:webhook: the target is the configured API base, not
            // an author-supplied URL, so there is no SSRF surface to protect.
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                $"{_options.ApiBaseUrl.TrimEnd('/')}/bot{_options.BotToken}/sendMessage",
                new { chat_id = config.ChatId, text = config.Message },
                ct);

            string body = await response.Content.ReadAsStringAsync(ct);
            int status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement(status)
                });
            }

            // 4xx means the request itself is wrong - unknown chat, bad token - and will be wrong again
            // next time. 429 is the exception: Telegram rate-limits, and that is exactly what a retry is
            // for. The response body is not echoed, because it can quote the request URL, which carries
            // the bot token.
            bool retryable = status >= 500 || status == 429;
            return new NodeExecutionResult.Fail("TELEGRAM_HTTP_ERROR", $"Telegram API returned {status}.", retryable, null);
        }
        catch (HttpRequestException ex)
        {
            return new NodeExecutionResult.Fail("TELEGRAM_NETWORK_ERROR", ex.Message, true, ex);
        }
    }
}
