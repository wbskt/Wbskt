using System.Text.Json;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Clients;

internal sealed class WorkflowEngineClient : IWorkflowEngineClient
{
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

    private sealed record EngineManualResponse(string Outcome, Guid? RunRefId, long? RunId);

    private sealed record EngineSignalResponse(string Outcome, bool Matched);
}
