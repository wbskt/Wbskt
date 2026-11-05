using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Clients;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// Drops the commands waiting for a client that loses its registration: it will not sign in to take
/// them. Each ends as a failed command, so the console and a waiting workflow step hear why. A deleted
/// client's commands go with its row (dbo.Client_Delete).
/// </summary>
public sealed class ClientCommandQueueRevocationHandler : IConsumer<ClientStatusChangedEvent>
{
    private readonly ClientCommandQueue _queue;

    public ClientCommandQueueRevocationHandler(ClientCommandQueue queue)
    {
        _queue = queue;
    }

    public async Task Consume(ConsumeContext<ClientStatusChangedEvent> context)
    {
        if (context.Message.HasAccess)
        {
            return;
        }

        await _queue.DropAsync(context.Message.ClientId, ClientCommandFailedEvent.RevokedWhileQueuedReason, context.CancellationToken);
    }
}
