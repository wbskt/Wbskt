using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Clients;
using Wbskt.Management.Host.Providers;
using Wbskt.Protocol;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// Records a client's presence from its socket host's connect and disconnect events. On a connect it
/// also sends the commands that waited while the client was offline, addressed to the host now holding it.
/// </summary>
public sealed class ClientPresenceHandler : IConsumer<ClientConnectedEvent>, IConsumer<ClientDisconnectedEvent>
{
    private readonly IClientProvider _clientProvider;
    private readonly ClientCommandQueue _commandQueue;

    public ClientPresenceHandler(IClientProvider clientProvider, ClientCommandQueue commandQueue)
    {
        _clientProvider = clientProvider;
        _commandQueue = commandQueue;
    }

    public async Task Consume(ConsumeContext<ClientConnectedEvent> context)
    {
        var message = context.Message;
        await _clientProvider.UpdatePresenceAsync(message.ClientId, true, message.CreatedAtUtc, message.HostId, cancellationToken: context.CancellationToken);

        // After the presence write: a sender that queues from here on sees the client online and sends
        // live, and anything queued before it is taken here (see ClientCommandQueue.QueueAsync).
        await _commandQueue.DeliverAsync(message.ClientRefId, message.ClientId, message.WorkspaceId, message.HostId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ClientDisconnectedEvent> context)
    {
        var message = context.Message;
        DateTime? updatingUntil = message.IsUpdating ? message.CreatedAtUtc + OtaTiming.UpdateWindow : null;
        await _clientProvider.UpdatePresenceAsync(message.ClientId, false, message.CreatedAtUtc, message.HostId, updatingUntil, context.CancellationToken);
    }
}
