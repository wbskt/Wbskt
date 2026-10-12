using System.Net;
using System.Text.Json;
using Wbskt.Infrastructure;
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
        var response = await _httpClient.PostAsJsonAsync(EngineInboundApi.Manual(workflowRefId, request.IdempotencyKey), body, SerializerOptions, ct);
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
            _ => engineResponse?.RunRefId is null ? StartRunOutcome.NoManualTrigger : StartRunOutcome.Started
        };

        return new StartRunResponse(engineResponse?.RunRefId ?? Guid.Empty, outcome);
    }

    public async Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(EngineInboundApi.Signal(runRefId, signalName), request.Payload, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        InboundResponse? engineResponse = await response.Content.ReadFromJsonAsync<InboundResponse>(SerializerOptions, ct);

        return new SignalResponse(engineResponse?.Matched ?? false, engineResponse?.Outcome.ToString() ?? string.Empty);
    }

    /// <summary>What a caller is told when the engine cannot be asked at all.</summary>
    internal static readonly Error IntegrationsUnavailable = Error.Unavailable(
        "INTEGRATIONS_UNAVAILABLE",
        "Integrations cannot be changed or tested right now. Try again shortly.");

    public Task<Result<IntegrationSealResponse>> SealIntegrationAsync(IntegrationSealRequest request, CancellationToken ct) =>
        PostIntegrationAsync<IntegrationSealRequest, IntegrationSealResponse>(EngineIntegrationsApi.SealRoute, request, ct);

    public Task<Result<IntegrationProbeResponse>> ProbeIntegrationAsync(IntegrationProbeRequest request, CancellationToken ct) =>
        PostIntegrationAsync<IntegrationProbeRequest, IntegrationProbeResponse>(EngineIntegrationsApi.TestRoute, request, ct);

    private async Task<Result<TResponse>> PostIntegrationAsync<TRequest, TResponse>(string route, TRequest request, CancellationToken ct)
        where TResponse : class
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(route, request, SerializerOptions, ct);
        }
        catch (HttpRequestException)
        {
            return Result<TResponse>.Failure(IntegrationsUnavailable);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<TResponse>.Failure(IntegrationsUnavailable);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, ct);
                return body is null ? Result<TResponse>.Failure(IntegrationsUnavailable) : Result<TResponse>.Success(body);
            }

            // The engine's refusal is the caller's to read: a validation code for a bad integration,
            // INTEGRATIONS_NOT_CONFIGURED for an engine with no master key. Anything else (a wrong
            // inbound key, a crash) is this deployment's fault, not the caller's, and reads as unavailable.
            var (code, message) = await ReadProblemAsync(response, ct);
            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest when code is not null && code != Problems.ValidationFailed =>
                    Result<TResponse>.Failure(Error.Validation(code, message ?? "The integration is not valid.")),
                HttpStatusCode.ServiceUnavailable when code == "INTEGRATIONS_NOT_CONFIGURED" =>
                    Result<TResponse>.Failure(Error.Unavailable(code, message ?? IntegrationsUnavailable.Message)),
                _ => Result<TResponse>.Failure(IntegrationsUnavailable)
            };
        }
    }

    private static async Task<(string? Code, string? Message)> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(SerializerOptions, ct);
            string? code = problem.TryGetProperty(Problems.CodeKey, out var c) ? c.GetString() : null;
            string? message = problem.TryGetProperty("detail", out var d) ? d.GetString() : null;
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
