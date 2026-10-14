using System.Text.Json;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>
/// Everything this host asks of the workflow engine goes through here. The engine is the only writer
/// of run state. Starting and signalling runs are relayed to it over HTTP, because the caller is answered
/// with the outcome. Public webhook and wake callbacks, and cancelling a run, are commands on the bus,
/// carried out by whichever engine instance takes them.
/// Reads (run lists, history, stats) are not engine operations and stay direct queries.
/// </summary>
/// <remarks>
/// Publishing a definition and registering its triggers still happen in this host, not through here.
/// The engine reads definitions and trigger registrations through caches of its own, which this host
/// does not reach into: after each change it publishes a <c>WorkflowDefinitionChangedEvent</c>, and the
/// engine drops the cached entries when it arrives (entries also expire on their own, for an event that
/// arrives late or never).
/// </remarks>
public interface IWorkflowEngineGateway
{
    Task<StartRunResponse> StartManualRunAsync(Guid workflowRefId, StartRunRequest request, CancellationToken ct);

    Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct);

    /// <summary>
    /// Sends a public wake callback to the engine's callback queue and returns once the broker has it.
    /// Throws when the broker fails or does not answer in time, like <see cref="CancelRunAsync"/>.
    /// </summary>
    Task SendWakeAsync(string token, JsonElement payload, CancellationToken ct);

    /// <summary>
    /// Sends a public webhook callback to the engine's callback queue, as <see cref="SendWakeAsync"/>.
    /// <c>secret</c> is whatever the anonymous caller presented in <c>X-Wbskt-Secret</c>, passed through
    /// verbatim so the engine can compare it against the trigger's configured secret; null when the
    /// caller sent none.
    /// </summary>
    Task SendWebhookAsync(Guid workspaceRef, string path, JsonElement payload, string? secret, string? idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Sends the engine a command to cancel the run, on the real bus, and returns once the broker has it.
    /// The cancel itself happens later, on the engine. Throws when the broker fails or does not answer
    /// in time, because the send is the whole action: a caller reporting success here would be
    /// reporting a cancel that may never reach the engine.
    /// </summary>
    Task CancelRunAsync(long runId, string reason, CancellationToken ct);

    /// <summary>
    /// Queues the same command to go out once the broker takes it, for a cancel that follows a change
    /// already committed (a deleted workflow): the change stands either way, so a broker outage must
    /// delay its runs' cancellation rather than fail the request.
    /// </summary>
    Task QueueCancelRunAsync(long runId, string reason, CancellationToken ct);
}
