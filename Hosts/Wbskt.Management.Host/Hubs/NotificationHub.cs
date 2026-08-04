using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    /// <summary>
    /// Reference-to-internal-ID map for the workspaces this connection has joined, held in
    /// <see cref="HubCallerContext.Items"/> so it lives as long as the connection does.
    /// <see cref="LeaveWorkspace"/> reads it rather than resolving again — see there for why.
    /// </summary>
    private const string JoinedWorkspacesKey = "joined-workspaces";

    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

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
            throw new HubException(CallerSafeMessage(workspaceIdResult.Error));
        }

        JoinedWorkspaces[parsedRef] = workspaceIdResult.Value;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(workspaceIdResult.Value));
    }

    /// <summary>
    /// Unsubscribes the caller from a workspace's feed.
    /// <para>
    /// Resolved from what this connection joined rather than by asking the auth service again. A
    /// permission check here would be the wrong way round: it can only ever fail *after* a
    /// successful join — the member was removed, or <c>workspace.join</c> was revoked — and failing
    /// it would strand the connection in a group it can no longer ask to leave. The only group name
    /// it can produce is one the caller was already admitted to.
    /// </para>
    /// </summary>
    public async Task LeaveWorkspace(string workspaceRef)
    {
        if (!Guid.TryParse(workspaceRef, out var parsedRef))
        {
            throw new HubException("Workspace reference is not a valid identifier.");
        }

        if (!JoinedWorkspaces.TryRemove(parsedRef, out var workspaceId))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(workspaceId));
    }

    /// <summary>The broadcast group for a workspace. Must stay in sync with <c>SignalRForwardingHandler</c>.</summary>
    internal static string GroupName(int workspaceId) => $"ws:{workspaceId}";

    private ConcurrentDictionary<Guid, int> JoinedWorkspaces
    {
        get
        {
            // Concurrent because a client may have several invocations in flight on one connection.
            if (Context.Items.TryGetValue(JoinedWorkspacesKey, out var existing) && existing is ConcurrentDictionary<Guid, int> joined)
            {
                return joined;
            }

            var created = new ConcurrentDictionary<Guid, int>();
            Context.Items[JoinedWorkspacesKey] = created;
            return created;
        }
    }

    /// <summary>
    /// Mirrors <c>ApiControllerBase</c>: an <see cref="ErrorType.Failure"/> message is typically raw
    /// exception text and this host is publicly routed, so it is logged and replaced. The other
    /// arms are application-authored and safe to hand back.
    /// </summary>
    private string CallerSafeMessage(Error error)
    {
        if (error.Type != ErrorType.Failure)
        {
            _logger.LogWarning("Hub join refused: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);
            return error.Message;
        }

        _logger.LogError("Hub join failed: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);
        return "An unexpected error occurred while joining the workspace.";
    }
}
