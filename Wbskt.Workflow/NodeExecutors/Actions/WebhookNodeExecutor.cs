using System.Net.Http;
using System.Text;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

public sealed class WebhookNodeExecutor(IHttpClientFactory httpClientFactory) : INodeExecutor
{
    public string Kind => NodeKind.ActionWebhook;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        WebhookNotificationNode node = (WebhookNotificationNode)ctx.Node;
        HttpClient client = httpClientFactory.CreateClient("workflow-webhook");

        try
        {
            HttpContent? content = null;
            if (node.Config.Body.HasValue)
            {
                content = new StringContent(node.Config.Body.Value.GetRawText(), Encoding.UTF8, "application/json");
            }

            using var request = new HttpRequestMessage(new HttpMethod(node.Config.Method), node.Config.Url)
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
