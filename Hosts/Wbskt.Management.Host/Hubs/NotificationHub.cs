using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    public async Task JoinWorkspace(string workspaceRef)
    {
        // The user is already authenticated by the [Authorize] attribute.
        // We can resolve scoped services directly from the Hub's context.
        var authClient = Context.GetHttpContext()!
            .RequestServices.GetRequiredService<IAuthServiceClient>();
            
        // The AuthenticationForwardingHandler will automatically add the user's token.
        var workspaceId = await authClient.ResolveWorkspaceAsync(Guid.Parse(workspaceRef), "workspaces:join");

        // If the above call fails, it will throw, and the user won't be added to the group.
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ws:{workspaceId}");
    }

    public async Task LeaveWorkspace(int workspaceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ws:{workspaceId}");
    }
}
