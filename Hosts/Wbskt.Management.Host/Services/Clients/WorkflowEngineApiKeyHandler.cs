namespace Wbskt.Management.Host.Services.Clients;

// Sends the shared key the engine's InboundApiKeyMiddleware expects on every api/inbound/* call.
public sealed class WorkflowEngineApiKeyHandler : DelegatingHandler
{
    private const string ApiKeyHeaderName = "X-Wbskt-Api-Key";

    private readonly string? _apiKey;

    public WorkflowEngineApiKeyHandler(IConfiguration configuration)
    {
        _apiKey = configuration["Services:WorkflowEngineApiKey"];
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Add(ApiKeyHeaderName, _apiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
