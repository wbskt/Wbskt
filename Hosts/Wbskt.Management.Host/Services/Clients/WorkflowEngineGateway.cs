using System.Text.Json;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure.Events;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Clients;

internal sealed class WorkflowEngineGateway : IWorkflowEngineGateway
{
    /// <summary>How long a cancel command waits for the broker before the caller is told to retry.</summary>
    internal static readonly TimeSpan CommandPublishTimeout = TimeSpan.FromSeconds(5);

    private readonly IWorkflowEngineClient _engineClient;
    private readonly IEventBus _eventBus;
    private readonly IEventBus _queuedEventBus;

    public WorkflowEngineGateway(
        IWorkflowEngineClient engineClient,
        IEventBus eventBus,
        [FromKeyedServices(QueuedEventBusExtensions.QueuedKey)] IEventBus queuedEventBus)
    {
        _engineClient = engineClient;
        _eventBus = eventBus;
        _queuedEventBus = queuedEventBus;
    }

    public Task<StartRunResponse> StartManualRunAsync(Guid workflowRefId, StartRunRequest request, CancellationToken ct)
    {
        return _engineClient.StartManualRunAsync(workflowRefId, request, ct);
    }

    public Task<SignalResponse> SignalAsync(Guid runRefId, string signalName, SignalRequest request, CancellationToken ct)
    {
        return _engineClient.SignalAsync(runRefId, signalName, request, ct);
    }

    public Task<WakeResponse> WakeAsync(string token, JsonElement payload, CancellationToken ct)
    {
        return _engineClient.WakeAsync(token, payload, ct);
    }

    public Task<WebhookResponse> WebhookAsync(Guid workspaceRef, string path, JsonElement payload, string? secret, string? idempotencyKey, CancellationToken ct)
    {
        return _engineClient.WebhookAsync(workspaceRef, path, payload, secret, idempotencyKey, ct);
    }

    public async Task CancelRunAsync(long runId, string reason, CancellationToken ct)
    {
        // The real bus, not the queued one: a queued command would be reported as sent when it might
        // never go out. Bounded, so a broker that accepts the connection and never answers is reported
        // as unavailable rather than holding the request.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandPublishTimeout);

        await _eventBus.PublishAsync(new CancelWorkflowRun(runId, reason), timeout.Token);
    }

    public Task QueueCancelRunAsync(long runId, string reason, CancellationToken ct)
    {
        return _queuedEventBus.PublishAsync(new CancelWorkflowRun(runId, reason), ct);
    }
}
