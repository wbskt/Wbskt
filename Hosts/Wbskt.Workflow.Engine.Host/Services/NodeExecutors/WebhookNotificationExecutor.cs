using System.Text;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class WebhookNotificationExecutor : IWorkflowNodeExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookNotificationExecutor> _logger;

    public WebhookNotificationExecutor(IHttpClientFactory httpClientFactory, ILogger<WebhookNotificationExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        var webhookNode = (WebhookNotificationNode)node;

        if (string.IsNullOrWhiteSpace(webhookNode.Url))
        {
            return NodeExecutionResult.Fail("Webhook URL is empty.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var method = new HttpMethod(webhookNode.Method ?? "POST");
            var request = new HttpRequestMessage(method, webhookNode.Url);

            if (!string.IsNullOrWhiteSpace(webhookNode.Payload))
            {
                request.Content = new StringContent(webhookNode.Payload, Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);
            
            _logger.LogInformation("Webhook sent to {Url}, Status: {StatusCode}", webhookNode.Url, response.StatusCode);

            // Webhooks often have a specific port for the response
            return NodeExecutionResult.Success(PortNames.OnResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send webhook to {Url}", webhookNode.Url);
            return NodeExecutionResult.Fail($"Webhook failed: {ex.Message}");
        }
    }
}
