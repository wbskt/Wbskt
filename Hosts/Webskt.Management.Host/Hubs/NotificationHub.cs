using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Webskt.Management.Host.Services.Clients;

namespace Webskt.Management.Host.Hubs;

// Interface for strongly-typed client methods
public interface INotificationClient
{
    Task ReceiveLatencyMeasurement(Guid clientRefId, double roundTripMs);
    // Add more client methods for other event types here
}

[Authorize]
public class NotificationHub : Hub<INotificationClient>
{
    public async Task JoinWorkspace(string workspaceRef)
    {
        // The user is already authenticated by the [Authorize] attribute.
        // We can resolve scoped services directly from the Hub's context.
        var authClient = Context.GetHttpContext()!
            .RequestServices.GetRequiredService<IAuthServiceClient>();
            
        // The AuthenticationForwardingHandler will automatically add the user's token.
        await authClient.ResolveWorkspaceAsync(Guid.Parse(workspaceRef), "workspaces:join");

        // If the above call fails, it will throw, and the user won't be added to the group.
        await Groups.AddToGroupAsync(Context.ConnectionId, workspaceRef);
    }

    public async Task LeaveWorkspace(string workspaceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, workspaceId);
    }
}
