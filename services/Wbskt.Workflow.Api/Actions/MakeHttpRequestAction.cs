using System.Text.Json;
using Wbskt.Common.Services;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Actions;

public class MakeHttpRequestAction : IAction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialService _credentialService;
    private readonly ILogger<MakeHttpRequestAction> _logger;

    public MakeHttpRequestAction(IHttpClientFactory httpClientFactory, ICredentialService credentialService, ILogger<MakeHttpRequestAction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentialService = credentialService;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing MakeHttpRequestAction.");

        // In a real implementation, this would deserialize a proper StepConfiguration object.
        // and use a templating engine.

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/data");
        request.Content = new StringContent("{ \"message\": \"Hello from WBSKT Workflow!\" }", System.Text.Encoding.UTF8, "application/json");

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
