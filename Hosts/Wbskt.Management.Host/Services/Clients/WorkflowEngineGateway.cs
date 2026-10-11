using System.Text.Json;
using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure.Events;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Clients;

internal sealed class WorkflowEngineGateway : IWorkflowEngineGateway
{
    /// <summary>How long a command waits for the broker before the caller is told to retry.</summary>
    internal static readonly TimeSpan CommandPublishTimeout = TimeSpan.FromSeconds(5);

    private readonly IWorkflowEngineClient _engineClient;
    private readonly IEventBus _eventBus;
    private readonly IEventBus _queuedEventBus;
    private readonly ISendEndpointProvider _sendEndpointProvider;

    public WorkflowEngineGateway(
        IWorkflowEngineClient engineClient,
        IEventBus eventBus,
        [FromKeyedServices(QueuedEventBusExtensions.QueuedKey)] IEventBus queuedEventBus,
        ISendEndpointProvider sendEndpointProvider)
    {
        _engineClient = engineClient;
        _eventBus = eventBus;
        _queuedEventBus = queuedEventBus;
        _sendEndpointProvider = sendEndpointProvider;
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
        return SendCallbackAsync(new InboundWake(token, payload.GetRawText()), ct);
    }

    public Task SendWebhookAsync(Guid workspaceRef, string path, JsonElement payload, string? secret, string? idempotencyKey, CancellationToken ct)
    {
        // An empty header is no secret, as when it was relayed over HTTP.
        return SendCallbackAsync(new InboundWebhook(workspaceRef, path, payload.GetRawText(), string.IsNullOrEmpty(secret) ? null : secret, idempotencyKey), ct);
    }

    private async Task SendCallbackAsync<T>(T callback, CancellationToken ct) where T : class
    {
        // Sent to the engine's own queue rather than published: these are not events, and the event
        // log would otherwise keep every anonymous caller's body and presented secret.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandPublishTimeout);

        ISendEndpoint endpoint = await _sendEndpointProvider.GetSendEndpoint(InboundCallbackQueue.Address);
        await endpoint.Send(callback, timeout.Token);
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
