using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    /// <summary>
    /// Subscribes the caller to a workspace's realtime feed. The group name must match the one
    /// <c>SignalRForwardingHandler</c> broadcasts to — <c>ws:{internal workspace id}</c> — so the
    /// reference has to be resolved to its internal ID first, and the caller must hold
    /// <see cref="Permissions.WorkspaceJoin"/> in it.
    /// </summary>
    public async Task JoinWorkspace(string workspaceRef)
    {
        if (!Guid.TryParse(workspaceRef, out var parsedRef))
        {
            throw new HubException("Workspace reference is not a valid identifier.");
        }

        // The user is already authenticated by the [Authorize] attribute.
        // We can resolve scoped services directly from the Hub's context.
        var authClient = Context.GetHttpContext()!
            .RequestServices.GetRequiredService<IAuthServiceClient>();

        // The AuthenticationForwardingHandler will automatically add the user's token.
        var workspaceIdResult = await authClient.ResolveWorkspaceAsync(parsedRef, Permissions.WorkspaceJoin);

        // Resolution reports a denial as a failed Result, it does not throw. Left unchecked the
        // connection joins a group regardless of the answer, which is both a permission bypass and
        // an interpolation of the Result object itself into the group name.
        if (workspaceIdResult.IsFailure)
        {
            throw new HubException(workspaceIdResult.Error.Message);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(workspaceIdResult.Value));
    }

    /// <summary>
    /// Unsubscribes the caller from a workspace's feed. Takes the same reference as
    /// <see cref="JoinWorkspace"/>; it is ungated because leaving only ever removes the caller's own
    /// connection from a group, and an unresolvable reference simply removes nothing.
    /// </summary>
    public async Task LeaveWorkspace(string workspaceRef)
    {
        if (!Guid.TryParse(workspaceRef, out var parsedRef))
        {
            throw new HubException("Workspace reference is not a valid identifier.");
        }

        var authClient = Context.GetHttpContext()!
            .RequestServices.GetRequiredService<IAuthServiceClient>();

        var workspaceIdResult = await authClient.ResolveWorkspaceAsync(parsedRef, Permissions.WorkspaceJoin);
        if (workspaceIdResult.IsFailure)
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(workspaceIdResult.Value));
    }

    /// <summary>The broadcast group for a workspace. Must stay in sync with <c>SignalRForwardingHandler</c>.</summary>
    internal static string GroupName(int workspaceId) => $"ws:{workspaceId}";
}
