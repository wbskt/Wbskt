using System.Net.Http.Json;
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
        var response = await _httpClient.PostAsJsonAsync($"api/inbound/manual/{workflowRefId}", body, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        EngineManualResponse? engineResponse = await response.Content.ReadFromJsonAsync<EngineManualResponse>(SerializerOptions, ct);

        if (engineResponse?.RunRefId is null)
        {
            throw new InvalidOperationException("Manual trigger did not start a run.");
        }

        return new StartRunResponse(engineResponse.RunRefId.Value, engineResponse.RunId!.Value);
    }

    public async Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/inbound/signal/{runRefId}:{signalName}", request.Payload, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        EngineSignalResponse? engineResponse = await response.Content.ReadFromJsonAsync<EngineSignalResponse>(SerializerOptions, ct);

        return new SignalResponse(engineResponse?.Matched ?? false, engineResponse?.Outcome ?? string.Empty);
    }

    private sealed record EngineManualResponse(string Outcome, Guid? RunRefId, long? RunId);

    private sealed record EngineSignalResponse(string Outcome, bool Matched);
}
