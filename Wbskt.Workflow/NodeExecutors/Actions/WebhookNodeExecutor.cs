using System.Text;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

internal sealed class WebhookNodeExecutor(IHttpClientFactory httpClientFactory, IOutboundAddressGuard addressGuard) : INodeExecutor
{
    public string Kind => NodeKind.ActionWebhook;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        WebhookNotificationNode node = (WebhookNotificationNode)ctx.Node;

        // The URL is author-defined; refuse anything that isn't a well-formed absolute http(s) URL
        // pointing at a non-reserved address, so the engine can't be turned into an SSRF proxy.
        if (!Uri.TryCreate(node.Config.Url, UriKind.Absolute, out Uri? targetUri))
        {
            return new NodeExecutionResult.Fail("WEBHOOK_INVALID_URL", $"'{node.Config.Url}' is not a valid absolute URL.", false, null);
        }

        OutboundAddressDecision decision = await addressGuard.EvaluateAsync(targetUri, ct);
        if (!decision.Allowed)
        {
            return new NodeExecutionResult.Fail("WEBHOOK_BLOCKED_TARGET", decision.Reason ?? "target address is not allowed.", false, null);
        }

        HttpClient client = httpClientFactory.CreateClient("workflow-webhook");

        try
        {
            HttpContent? content = null;
            if (node.Config.Body.HasValue)
            {
                content = new StringContent(node.Config.Body.Value.GetRawText(), Encoding.UTF8, "application/json");
            }

            using var request = new HttpRequestMessage(new HttpMethod(node.Config.Method), targetUri)
            {
                Content = content
            };

            using HttpResponseMessage response = await client.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            int status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement(status),
                    ["body"] = JsonSerializer.SerializeToElement(body)
                });
            }

            return new NodeExecutionResult.Fail("WEBHOOK_HTTP_ERROR", $"{status}: {body}", status >= 500, null);
        }
        catch (HttpRequestException ex)
        {
            return new NodeExecutionResult.Fail("WEBHOOK_NETWORK_ERROR", ex.Message, true, ex);
        }
    }
}
