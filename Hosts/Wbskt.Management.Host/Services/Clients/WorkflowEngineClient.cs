using System.Text.Json;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Clients;

internal sealed class WorkflowEngineClient : IWorkflowEngineClient
{
    /// <summary>Must match <c>InboundWebhookController.SecretHeader</c> on the engine side.</summary>
    internal const string WebhookSecretHeader = "X-Wbskt-Secret";
    internal const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public WorkflowEngineClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<StartRunResponse> StartManualRunAsync(Guid workflowRefId, StartRunRequest request, CancellationToken ct)
    {
        object body = (object?)request.Payload ?? new { };
        string url = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"api/inbound/manual/{workflowRefId}"
            : $"api/inbound/manual/{workflowRefId}?idempotencyKey={Uri.EscapeDataString(request.IdempotencyKey)}";

        var response = await _httpClient.PostAsJsonAsync(url, body, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        EngineManualResponse? engineResponse = await response.Content.ReadFromJsonAsync<EngineManualResponse>(SerializerOptions, ct);

        // Not starting a run is frequently the correct outcome - a concurrency policy queued or
        // dropped the request, or an idempotency key deduplicated a retry. These are reported, not
        // thrown, so the controller can answer with something other than "500 something broke".
        StartRunOutcome outcome = engineResponse?.Outcome switch
        {
            "StartedRun" => StartRunOutcome.Started,
            "Idempotent" => StartRunOutcome.Duplicate,
            "Queued" => StartRunOutcome.Queued,
            "Dropped" => StartRunOutcome.Dropped,
            "NoRegistration" => StartRunOutcome.NoManualTrigger,
            _ => engineResponse?.RunRefId is null ? StartRunOutcome.NoManualTrigger : StartRunOutcome.Started
        };

        return new StartRunResponse(
            engineResponse?.RunRefId ?? Guid.Empty,
            engineResponse?.RunId ?? 0,
            outcome);
    }

    public async Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/inbound/signal/{runRefId}/{signalName}", request.Payload, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        EngineSignalResponse? engineResponse = await response.Content.ReadFromJsonAsync<EngineSignalResponse>(SerializerOptions, ct);

        return new SignalResponse(engineResponse?.Matched ?? false, engineResponse?.Outcome ?? string.Empty);
    }

    public async Task<WakeResponse> WakeAsync(string token, JsonElement payload, CancellationToken ct)
    {
        // The token is a path segment (author-defined string, or a run RefId); escape it. The
        // engine's /api/inbound/wake is backend-only and api-key gated - WorkflowEngineApiKeyHandler
        // adds the shared key, so this relays the public callback inward without exposing the engine.
        var response = await _httpClient.PostAsJsonAsync($"api/inbound/wake/{Uri.EscapeDataString(token)}", payload, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        EngineWakeResponse? engineResponse = await response.Content.ReadFromJsonAsync<EngineWakeResponse>(SerializerOptions, ct);

        return new WakeResponse(engineResponse?.Matched ?? false, engineResponse?.Outcome ?? string.Empty);
    }

    public async Task<WebhookResponse> WebhookAsync(Guid workspaceRef, string path, JsonElement payload, string? secret, string? idempotencyKey, CancellationToken ct)
    {
        // Same relay shape as WakeAsync: the engine's /api/inbound/webhook is backend-only and
        // api-key gated (WorkflowEngineApiKeyHandler adds the key), so this fronts an external
        // webhook publicly without exposing the engine. The webhook is workspace-scoped so a path is
        // unique per workspace; the caller-supplied path segment is escaped.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/inbound/webhook/{workspaceRef}/{Uri.EscapeDataString(path)}")
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };

        // Relayed as a header rather than folded into the body: the body becomes the run's persisted
        // trigger payload, and the secret must not end up in workflow state or the history trace.
        // TryAddWithoutValidation because the caller controls this value and a malformed one should be
        // rejected by the engine's comparison, not throw here.
        if (!string.IsNullOrEmpty(secret))
        {
            request.Headers.TryAddWithoutValidation(WebhookSecretHeader, secret);
        }

        // The engine validates it and turns it into the event id the delivery is deduplicated on.
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, idempotencyKey);
        }

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        EngineWebhookResponse? engineResponse = await response.Content.ReadFromJsonAsync<EngineWebhookResponse>(SerializerOptions, ct);

        return new WebhookResponse(engineResponse?.Outcome ?? string.Empty, engineResponse?.RunId);
    }

    private sealed record EngineManualResponse(string Outcome, Guid? RunRefId, long? RunId);

    private sealed record EngineSignalResponse(string Outcome, bool Matched);

    private sealed record EngineWakeResponse(string Outcome, bool Matched);

    private sealed record EngineWebhookResponse(string Outcome, Guid? RunId);
}
