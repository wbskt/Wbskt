using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// A credential for one HTTP origin (a Home Assistant instance, an API with a key), which Call a URL
/// adds to its request. The base URL is the owner's choice, so it goes through the address guard.
/// </summary>
internal sealed class HttpAuthChannel(IHttpClientFactory httpClientFactory, IOutboundAddressGuard addressGuard) : IIntegrationChannel
{
    private const string ServiceName = "The server";

    public IntegrationType Type => IntegrationType.HttpAuth;

    public async Task<ChannelResult> TestAsync(ResolvedIntegration integration, IntegrationTestOptions? options, CancellationToken ct)
    {
        var settings = (HttpAuthSettings)integration.Settings;
        bool homeAssistant = integration.Preset == IntegrationPreset.HomeAssistant;

        // Home Assistant's /api/config needs a valid token and says which instance answered. Anything
        // else gets a GET of its base URL with the credential on it.
        using var request = new HttpRequestMessage(HttpMethod.Get, homeAssistant ? settings.BaseUrl + "/api/config" : settings.BaseUrl);
        Authorize(request, integration);

        var (response, failure) = await HttpChannelSupport.SendAsync(httpClientFactory.CreateClient(HttpChannelSupport.ClientName), request, addressGuard, homeAssistant ? "Home Assistant" : ServiceName, ct);
        if (failure is not null)
        {
            return Scrubbed(failure.ForTest(), integration);
        }

        using (response)
        {
            if (!response!.IsSuccessStatusCode)
            {
                return Scrubbed(HttpChannelSupport.FromStatus(response.StatusCode, homeAssistant ? "Home Assistant" : ServiceName).ForTest(), integration);
            }

            JsonElement? metadata = null;
            if (homeAssistant)
            {
                try
                {
                    var config = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                    metadata = JsonSerializer.SerializeToElement(new
                    {
                        version = config.TryGetProperty("version", out var v) ? v.GetString() : null,
                        locationName = config.TryGetProperty("location_name", out var l) ? l.GetString() : null
                    });
                }
                catch (JsonException)
                {
                }
            }

            return ChannelResult.Success((int)response.StatusCode, metadata);
        }
    }

    /// <summary>Puts the integration's credential on <paramref name="request"/>, in the header its mode names.</summary>
    public static void Authorize(HttpRequestMessage request, ResolvedIntegration integration)
    {
        var settings = (HttpAuthSettings)integration.Settings;
        var secrets = integration.Secrets;
        switch (settings.Mode)
        {
            case HttpAuthSettings.Bearer:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secrets.Token);
                break;
            case HttpAuthSettings.Basic:
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.UserName}:{secrets.Password}")));
                break;
            default:
                request.Headers.Remove(settings.HeaderName!);
                request.Headers.TryAddWithoutValidation(settings.HeaderName!, secrets.HeaderValue);
                break;
        }
    }

    /// <summary>
    /// Where a step's URL goes with this integration: a relative URL is appended to the base URL; an
    /// absolute one must have the base URL's origin (scheme, host and port), or the credential would
    /// travel somewhere its owner did not point it. Null when it may not be sent.
    /// </summary>
    public static Uri? Target(HttpAuthSettings settings, string url)
    {
        var baseUri = new Uri(settings.BaseUrl!.TrimEnd('/') + "/");
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            return Uri.Compare(absolute, baseUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0
                ? absolute
                : null;
        }

        return Uri.TryCreate(baseUri, url.TrimStart('/'), out var combined) && combined.Scheme == baseUri.Scheme && combined.Authority == baseUri.Authority
            ? combined
            : null;
    }

    private static ChannelResult Scrubbed(ChannelResult result, ResolvedIntegration integration) =>
        result with { Message = IntegrationErrors.Scrub(result.Message, integration.Secrets) };
}
