using System.Text.Json;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure.Events;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Management.Host.Services.Clients;

internal sealed class WorkflowEngineGateway : IWorkflowEngineGateway
{
    /// <summary>How long a command waits for the broker before the caller is told to retry.</summary>
    internal static readonly TimeSpan CommandPublishTimeout = TimeSpan.FromSeconds(5);

    private readonly IWorkflowEngineClient _engineClient;
    private readonly ICommandSender _commands;
    private readonly ICommandSender _queuedCommands;

    public WorkflowEngineGateway(
        IWorkflowEngineClient engineClient,
        ICommandSender commands,
        [FromKeyedServices(QueuedEventBusExtensions.QueuedKey)] ICommandSender queuedCommands)
    {
        _engineClient = engineClient;
        _commands = commands;
        _queuedCommands = queuedCommands;
    }

    public Task<StartRunResponse> StartManualRunAsync(Guid workflowRefId, StartRunRequest request, CancellationToken ct)
    {
        return _engineClient.StartManualRunAsync(workflowRefId, request, ct);
    }

    public Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct)
    {
        return _engineClient.SignalAsync(runRefId, signalName, request, ct);
    }

    public Task SendWakeAsync(string token, JsonElement payload, CancellationToken ct)
    {
        return SendCallbackAsync(new WakeWaitingStep(token, payload.GetRawText()), ct);
    }

    public Task SendWebhookAsync(Guid workspaceRef, string path, JsonElement payload, string? secret, string? idempotencyKey, CancellationToken ct)
    {
        // Hashed before it is queued, so the secret is never written to the broker. An empty header is
        // no secret.
        return SendCallbackAsync(new TriggerWebhook(workspaceRef, path, payload.GetRawText(), WebhookSecretHash.Of(secret), idempotencyKey), ct);
    }

    // Sent to the engine's own queue: these are commands, and the event log would otherwise keep
    // every anonymous caller's body and presented secret.
    private Task SendCallbackAsync<T>(T callback, CancellationToken ct) where T : class, ICommand => SendWithTimeoutAsync(callback, ct);

    public Task CancelRunAsync(long runId, string reason, CancellationToken ct)
    {
        // Sent now, not queued: a queued command would be reported as sent when it might never go out.
        return SendWithTimeoutAsync(new CancelWorkflowRun(runId, reason), ct);
    }

    public Task QueueCancelRunAsync(long runId, string reason, CancellationToken ct)
    {
        return _queuedCommands.SendAsync(new CancelWorkflowRun(runId, reason), ct);
    }

    // Bounded, so a broker that accepts the connection and never answers is reported as unavailable
    // rather than holding the request.
    private async Task SendWithTimeoutAsync<T>(T command, CancellationToken ct) where T : class, ICommand
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandPublishTimeout);

        await _commands.SendAsync(command, timeout.Token);
    }
}
