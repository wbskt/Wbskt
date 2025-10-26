using Wbskt.Common.Configurations;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Actions;

public class MakeHttpRequestAction : IAction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MakeHttpRequestAction> _logger;

    public MakeHttpRequestAction(IHttpClientFactory httpClientFactory, ILogger<MakeHttpRequestAction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(StepConfigurationBase configuration, WorkflowContext context, CancellationToken cancellationToken)
    {
        var config = configuration as MakeHttpRequestConfiguration;
        _logger.LogInformation("Executing MakeHttpRequestAction.");

        // In a real implementation, this would use a templating engine.

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(config.Method), config.Url);

        if (!string.IsNullOrEmpty(config.Body))
        {
            request.Content = new StringContent(config.Body, System.Text.Encoding.UTF8, "application/json");
        }

        // Header and auth logic would be added here

        var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("HTTP Request failed with status {StatusCode}: {Content}", response.StatusCode, errorContent);
            return new ActionResult(false, $"HTTP request failed with status {response.StatusCode}");
        }

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        context.Properties[$"step_{context.WorkflowExecutionId}_response"] = responseContent;
        _logger.LogInformation("HTTP Request successful. Response added to context.");

        return new ActionResult(true);
    }
}
