using System.Text;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Call a URL: an HTTP request to an author-chosen URL, optionally with extra headers and with the
/// credential of an HTTP auth integration. Its output is the status, the body as text and, for a JSON
/// reply, the body parsed (<c>json</c>); the call is bounded by the step's timeout and the body by
/// <see cref="WebhookNotificationConfig.MaxResponseBytes"/>.
/// </summary>
internal sealed class WebhookNodeExecutor(IHttpClientFactory httpClientFactory, IOutboundAddressGuard addressGuard, IIntegrationResolver resolver) : INodeExecutor
{
    public string Kind => NodeKind.ActionWebhook;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        WebhookNotificationNode node = (WebhookNotificationNode)ctx.Node;
        WebhookNotificationConfig config = node.Config;

        ResolvedIntegration? integration = null;
        Uri? targetUri;
        // node.IntegrationRef, not config's: an empty GUID names no integration, as the validator reads it.
        if (node.IntegrationRef is { } integrationRef)
        {
            var resolution = await resolver.ResolveAsync(ctx.Branch, integrationRef, IntegrationType.HttpAuth, ct);
            if (resolution.Failure is not null)
            {
                return resolution.Failure;
            }

            integration = resolution.Integration!;
            var settings = (HttpAuthSettings)integration.Settings;

            // The credential goes only where its owner pointed it: the base URL's origin.
            targetUri = HttpAuthChannel.Target(settings, config.Url);
            if (targetUri is null)
            {
                return new NodeExecutionResult.Fail(
                    "WEBHOOK_ORIGIN_MISMATCH",
                    $"'{config.Url}' is not on {settings.BaseUrl}, the address the integration's credential is for.",
                    false,
                    null);
            }

            if (config.Headers?.Keys.Any(h => h.Equals(settings.AuthHeaderName, StringComparison.OrdinalIgnoreCase)) == true)
            {
                return new NodeExecutionResult.Fail(
                    "WEBHOOK_HEADER_CONFLICT",
                    $"The step sets '{settings.AuthHeaderName}', which the integration's credential goes in.",
                    false,
                    null);
            }
        }
        else if (!Uri.TryCreate(config.Url, UriKind.Absolute, out targetUri) || targetUri.Scheme is not ("http" or "https"))
        {
            // The URL is author-defined; refuse anything that isn't a well-formed absolute http(s) URL
            // pointing at a non-reserved address, so the engine can't be turned into an SSRF proxy.
            return new NodeExecutionResult.Fail("WEBHOOK_INVALID_URL", $"'{config.Url}' is not a valid absolute URL.", false, null);
        }

        OutboundAddressDecision decision = await addressGuard.EvaluateAsync(targetUri, ct);
        if (!decision.Allowed)
        {
            return new NodeExecutionResult.Fail("WEBHOOK_BLOCKED_TARGET", decision.Reason ?? "target address is not allowed.", false, null);
        }

        HttpClient client = httpClientFactory.CreateClient("workflow-webhook");
        int timeoutSeconds = config.TimeoutSeconds ?? WebhookNotificationConfig.DefaultTimeoutSeconds;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            HttpContent? content = null;
            if (config.Body.HasValue)
            {
                content = new StringContent(config.Body.Value.GetRawText(), Encoding.UTF8, "application/json");
            }

            using var request = new HttpRequestMessage(new HttpMethod(config.Method), targetUri)
            {
                Content = content
            };

            foreach (var (name, value) in config.Headers ?? new Dictionary<string, string>())
            {
                // A content header (Content-Type) belongs on the body; anything else on the request.
                if (!request.Headers.TryAddWithoutValidation(name, value) && content is not null)
                {
                    content.Headers.Remove(name);
                    content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            if (integration is not null)
            {
                HttpAuthChannel.Authorize(request, integration);
            }

            // Headers only, so the body is read under the cap below rather than buffered whole.
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            (string body, bool truncated) = await ReadCappedAsync(response.Content, timeout.Token);
            int status = (int)response.StatusCode;

            if (integration is not null)
            {
                await resolver.MarkUsedAsync(integration, ct);
            }

            if (response.IsSuccessStatusCode)
            {
                if (truncated)
                {
                    return new NodeExecutionResult.Fail(
                        "WEBHOOK_RESPONSE_TOO_LARGE",
                        $"{status}: the response is larger than {WebhookNotificationConfig.MaxResponseBytes / 1024} KiB.",
                        false,
                        null);
                }

                var output = new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement(status),
                    ["body"] = JsonSerializer.SerializeToElement(body)
                };

                // A JSON reply is also handed on parsed, so later steps can read its fields.
                if (IsJson(response.Content) && TryParse(body) is { } json)
                {
                    output["json"] = json;
                }

                return new NodeExecutionResult.Continue("default", output);
            }

            string detail = integration is null ? body : IntegrationErrors.Scrub(body, integration.Secrets);
            return new NodeExecutionResult.Fail("WEBHOOK_HTTP_ERROR", $"{status}: {detail}{(truncated ? "…" : "")}", status >= 500, null);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new NodeExecutionResult.Fail("WEBHOOK_TIMEOUT", $"No complete response within {timeoutSeconds} seconds.", true, null);
        }
        catch (HttpRequestException ex)
        {
            string message = integration is null ? ex.Message : IntegrationErrors.Scrub(ex.Message, integration.Secrets);
            return new NodeExecutionResult.Fail("WEBHOOK_NETWORK_ERROR", message, true, ex);
        }
    }

    /// <summary>
    /// Reads at most <see cref="WebhookNotificationConfig.MaxResponseBytes"/> of the body, and says
    /// whether there was more.
    /// </summary>
    private static async Task<(string Body, bool Truncated)> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using Stream stream = await content.ReadAsStreamAsync(ct);
        byte[] buffer = new byte[WebhookNotificationConfig.MaxResponseBytes + 1];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        bool truncated = read > WebhookNotificationConfig.MaxResponseBytes;
        Encoding encoding = EncodingOf(content);
        return (encoding.GetString(buffer, 0, Math.Min(read, WebhookNotificationConfig.MaxResponseBytes)), truncated);
    }

    private static Encoding EncodingOf(HttpContent content)
    {
        try
        {
            return content.Headers.ContentType?.CharSet is { Length: > 0 } charset ? Encoding.GetEncoding(charset.Trim('"')) : Encoding.UTF8;
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static bool IsJson(HttpContent content) =>
        content.Headers.ContentType?.MediaType is { } mediaType
        && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    private static JsonElement? TryParse(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
