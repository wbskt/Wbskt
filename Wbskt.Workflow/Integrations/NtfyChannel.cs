using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// An ntfy server and topic. The server is the integration owner's choice, so it goes through the
/// address guard like any author-chosen target.
/// </summary>
internal sealed class NtfyChannel(IHttpClientFactory httpClientFactory, IOutboundAddressGuard addressGuard) : IIntegrationChannel
{
    private const string ServiceName = "The ntfy server";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public IntegrationType Type => IntegrationType.Ntfy;

    public async Task<ChannelResult> TestAsync(ResolvedIntegration integration, IntegrationTestOptions? options, CancellationToken ct)
    {
        // ntfy has no way to check a topic's access without publishing, so the test is a notification.
        var result = await PublishAsync(integration, "Test notification from Wbskt: this topic is ready.", "Wbskt", null, null, ct);
        return result.Ok ? result : result with { Code = result.Code!.Replace("NTFY_", "INTEGRATION_", StringComparison.Ordinal).Replace("NETWORK_ERROR", "UNREACHABLE", StringComparison.Ordinal) };
    }

    public async Task<ChannelResult> PublishAsync(ResolvedIntegration integration, string message, string? title, int? priority, IReadOnlyList<string>? tags, CancellationToken ct)
    {
        var settings = (NtfySettings)integration.Settings;

        // JSON publishing goes to the server's root with the topic in the body.
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ServerUrl + "/")
        {
            Content = JsonContent.Create(new
            {
                topic = integration.Secrets.Topic,
                message,
                title = string.IsNullOrWhiteSpace(title) ? null : title,
                priority,
                tags = tags is { Count: > 0 } ? tags : null
            }, options: Json)
        };

        if (!string.IsNullOrEmpty(integration.Secrets.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", integration.Secrets.AccessToken);
        }

        var (response, failure) = await HttpChannelSupport.SendAsync(httpClientFactory.CreateClient(HttpChannelSupport.ClientName), request, addressGuard, ServiceName, ct);
        if (failure is not null)
        {
            return Scrubbed(failure.ForStep("NTFY"), integration);
        }

        using (response)
        {
            return response!.IsSuccessStatusCode
                ? ChannelResult.Success((int)response.StatusCode)
                : Scrubbed(HttpChannelSupport.FromStatus(response.StatusCode, ServiceName).ForStep("NTFY"), integration);
        }
    }

    private static ChannelResult Scrubbed(ChannelResult result, ResolvedIntegration integration) =>
        result with { Message = IntegrationErrors.Scrub(result.Message, integration.Secrets) };
}
