using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Services.Clients;

// Sends the shared key the auth host's [InternalApiKey] routes expect, for calls with no user token
// to forward (the deleted workspace sweep). Configured as Services:AuthInternalApiKey here and
// Internal:ApiKey on the auth host.
public sealed class AuthInternalApiKeyHandler : DelegatingHandler
{
    public const string ConfigKey = "Services:AuthInternalApiKey";

    private readonly string? _apiKey;

    public AuthInternalApiKeyHandler(IConfiguration configuration)
    {
        _apiKey = configuration[ConfigKey];
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Add(InternalRoutes.ApiKeyHeader, _apiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
