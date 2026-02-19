using Microsoft.AspNetCore.SignalR;
using Webskt.Events.Socket;

namespace Webskt.Management.Host.Hubs;

// Interface for strongly-typed client methods
public interface INotificationClient
{
    Task ReceiveLatencyMeasurement(Guid clientRefId, double roundTripMs);
    // Add more client methods for other event types here
}

public class NotificationHub : Hub<INotificationClient>
{
    // When a client connects, add them to a group based on their workspace.
    // The workspace ID should be passed as a query parameter or from the JWT claims.
    // For MVP, we'll assume the client passes the workspace ID when joining.
    public async Task JoinWorkspace(string workspaceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, workspaceId);
    }

    public async Task LeaveWorkspace(string workspaceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, workspaceId);
    }
}
