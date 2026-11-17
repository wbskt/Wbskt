using System.Text.Json;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Engine.Contracts;

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
        var response = await _httpClient.PostAsJsonAsync(EngineInboundApi.Manual(workflowRefId, request.IdempotencyKey, request.TriggerNodeId), body, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        InboundResponse? engineResponse = await response.Content.ReadFromJsonAsync<InboundResponse>(SerializerOptions, ct);

        // Not starting a run is frequently the correct outcome - a concurrency policy queued or
        // dropped the request, or an idempotency key deduplicated a retry. These are reported, not
        // thrown, so the controller can answer with something other than "500 something broke".
        StartRunOutcome outcome = engineResponse?.Outcome switch
        {
            InboundOutcome.StartedRun => StartRunOutcome.Started,
            InboundOutcome.Idempotent => StartRunOutcome.Duplicate,
            InboundOutcome.Queued => StartRunOutcome.Queued,
            InboundOutcome.Dropped => StartRunOutcome.Dropped,
            InboundOutcome.NoRegistration => StartRunOutcome.NoManualTrigger,
            InboundOutcome.Refused => ToRefusal(engineResponse),
            InboundOutcome.Busy => StartRunOutcome.Busy,
            _ => engineResponse?.RunRefId is null ? StartRunOutcome.NoManualTrigger : StartRunOutcome.Started
        };

        return new StartRunResponse(engineResponse?.RunRefId ?? Guid.Empty, outcome);
    }

    // A refusal names the plan limit; one without a reason (from an older engine, or replayed from a
    // claim an older engine settled) reads as the run limit.
    private static StartRunOutcome ToRefusal(InboundResponse response) =>
        response.Registrations.FirstOrDefault(r => r.Outcome == InboundOutcome.Refused)?.RefusalReason switch
        {
            InboundRefusalReason.CreditLimit => StartRunOutcome.PlanCreditLimitReached,
            InboundRefusalReason.WorkspaceCreditCap => StartRunOutcome.WorkspaceCreditCapReached,
            _ => StartRunOutcome.PlanRunLimitReached
        };

    public async Task<EngineTestRunResponse> StartTestRunAsync(EngineTestRunRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(EngineTestRunApi.Route, request, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<EngineTestRunResponse>(SerializerOptions, ct)
            ?? throw new InvalidOperationException("The engine answered a test run with no body.");
    }

    public async Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(EngineInboundApi.Signal(runRefId, signalName), request.Payload, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        InboundResponse? engineResponse = await response.Content.ReadFromJsonAsync<InboundResponse>(SerializerOptions, ct);

        return new SignalResponse(engineResponse?.Matched ?? false, engineResponse?.Outcome.ToString() ?? string.Empty);
    }
}
