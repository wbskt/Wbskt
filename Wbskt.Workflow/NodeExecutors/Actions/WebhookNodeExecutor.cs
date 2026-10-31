using System.Buffers;
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
internal sealed class WebhookNodeExecutor(IHttpClientFactory httpClientFactory, IOutboundAddressGuard addressGuard, IIntegrationResolver resolver, ActionTemplates templates) : INodeExecutor
{
    public string Kind => NodeKind.ActionWebhook;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        WebhookNotificationNode node = (WebhookNotificationNode)ctx.Node;
        // Filled in before the origin and address checks, so they judge where the call really goes. Failure
        // messages quote the URL as written: filled in, it may carry a $shared secret into history.
        WebhookNotificationConfig config = await templates.FillAsync(ctx, node.Config, ct);

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
                    $"'{node.Config.Url}' is not on {settings.BaseUrl}, the address the integration's credential is for.",
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
            return new NodeExecutionResult.Fail("WEBHOOK_INVALID_URL", $"'{node.Config.Url}' is not a valid absolute URL.", false, null);
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

                // A JSON reply is also handed on parsed, so later steps can read its fields. Without
                // one, an earlier call's json is dropped from the branch rather than left to be
                // read as this call's.
                if (IsJson(response.Content))
                {
                    if (TryParse(body, out string? error) is { } json)
                    {
                        output["json"] = json;
                        return new NodeExecutionResult.Continue("default", output);
                    }

                    // Said JSON and wasn't: later steps reading json.* would otherwise just get nothing.
                    // An empty body (a 200 with no content) is not a broken promise. The error names a
                    // position, never the body, which may hold what the endpoint returned for a credential.
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        ctx.Warnings.Add(new NodeWarning(HistoryEventKind.WebhookJsonUnparsable, new
                        {
                            status,
                            contentType = response.Content.Headers.ContentType?.MediaType,
                            error
                        }));
                    }
                }

                return new NodeExecutionResult.Continue("default", output, RemoveKeys: ["json"]);
            }

            string detail = integration is null ? body : IntegrationErrors.Scrub(body, integration.Secrets);
            return new NodeExecutionResult.Fail("WEBHOOK_HTTP_ERROR", $"{status}: {detail}{(truncated ? "…" : "")}", status >= 500, null);
        }
        // A timeout that fires mid-body can surface as the stream's IOException rather than a
        // cancellation; it is still a timeout.
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException
            && timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new NodeExecutionResult.Fail("WEBHOOK_TIMEOUT", $"No complete response within {timeoutSeconds} seconds.", true, null);
        }
        // IOException: the connection dropped while the body was being read.
        catch (Exception ex) when (ex is HttpRequestException or IOException)
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
        // One byte past the cap says whether there was more. Rented: a fresh buffer this size would
        // be a large-object-heap allocation on every call.
        const int limit = WebhookNotificationConfig.MaxResponseBytes + 1;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(limit);
        int read = 0;
        try
        {
            while (read < limit)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read, limit - read), ct);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            bool truncated = read > WebhookNotificationConfig.MaxResponseBytes;
            int length = Math.Min(read, WebhookNotificationConfig.MaxResponseBytes);
            // A byte order mark names the encoding and is not part of the text, as ReadAsStringAsync
            // reads it; left in, it would make a JSON reply fail to parse.
            (Encoding encoding, int bomLength) = DetectBom(buffer.AsSpan(0, length)) is { } bom ? bom : (EncodingOf(content), 0);
            return (encoding.GetString(buffer, bomLength, length - bomLength), truncated);
        }
        finally
        {
            // The body may hold what the endpoint returned for a credential; it does not outlive the call.
            buffer.AsSpan(0, read).Clear();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>The encoding a byte order mark at the start of the body names, and the mark's length.</summary>
    private static (Encoding Encoding, int Length)? DetectBom(ReadOnlySpan<byte> body) => body switch
    {
        [0xEF, 0xBB, 0xBF, ..] => (Encoding.UTF8, 3),
        // UTF-32 LE before UTF-16 LE: its mark starts with UTF-16 LE's.
        [0xFF, 0xFE, 0x00, 0x00, ..] => (Encoding.UTF32, 4),
        [0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4),
        [0xFF, 0xFE, ..] => (Encoding.Unicode, 2),
        [0xFE, 0xFF, ..] => (Encoding.BigEndianUnicode, 2),
        _ => null
    };

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

    private static JsonElement? TryParse(string body, out string? error)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            error = null;
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            error = ex.LineNumber is { } line
                ? $"Not valid JSON at line {line + 1}, byte {ex.BytePositionInLine + 1}."
                : "Not valid JSON.";
            return null;
        }
    }
}
