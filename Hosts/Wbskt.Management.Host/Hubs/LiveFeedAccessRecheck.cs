using Microsoft.AspNetCore.SignalR;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Models;

namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// Takes a console connection out of the feeds it may no longer read. Access is checked when a
/// connection joins a workspace or client feed; without this, someone removed from the workspace or
/// suspended in its tenant kept receiving its events until their token expired, while every HTTP call
/// of theirs was already refused. Whenever access may have changed (the auth host's announcement, which
/// also empties <see cref="WorkspaceAccessCache"/>), each joined feed the change can reach is resolved
/// again with the token its connection opened with, and a feed the connection is now refused is left
/// for it. A revoked token (sign out everywhere, a password change or reset, a deleted account) counts
/// too: the auth host answers 401 for it, so a console left open on a lost laptop stops receiving the
/// feed (#959).
/// </summary>
/// <remarks>
/// An announcement names the users or workspaces it touches (<see cref="AccessChange"/>), a revocation
/// its user or session, so a change to one member re-checks only that member's connections and those
/// joined to the workspace named; a change that can't be narrowed (a role's permissions, a frozen
/// tenant, an announcement from an auth host before #1001) still re-checks every one. Announcements that
/// arrive during a pass are folded into one more pass over everything they touched. An auth host that
/// does not answer leaves the feeds as they are: the token's expiry still bounds them.
/// </remarks>
internal sealed class LiveFeedAccessRecheck : IHostedService
{
    private const int Parallelism = 8;

    private readonly WorkspaceAccessCache _cache;
    private readonly AccessTokenRevocation _revocation;
    private readonly LiveFeedConnections _connections;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LiveFeedAccessRecheck> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _gate = new();
    private AccessChange? _pending;
    private bool _running;
    private Task _pass = Task.CompletedTask;

    public LiveFeedAccessRecheck(
        WorkspaceAccessCache cache,
        AccessTokenRevocation revocation,
        LiveFeedConnections connections,
        IHubContext<NotificationHub> hub,
        IServiceScopeFactory scopes,
        ILogger<LiveFeedAccessRecheck> logger)
    {
        _cache = cache;
        _revocation = revocation;
        _connections = connections;
        _hub = hub;
        _scopes = scopes;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cache.Cleared += Request;
        _revocation.Revoked += Request;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cache.Cleared -= Request;
        _revocation.Revoked -= Request;
        await _stopping.CancelAsync();
        await Pass.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    /// <summary>Asks for a pass over what <paramref name="change"/> touches; one already running takes it as one more pass after it.</summary>
    internal void Request(AccessChange change)
    {
        lock (_gate)
        {
            _pending = _pending is null ? change : _pending.Union(change);
            if (_running)
            {
                return;
            }

            _running = true;
            _pass = Task.Run(RunAsync);
        }
    }

    /// <summary>The pass in flight, for a test to wait on.</summary>
    internal Task Pass
    {
        get
        {
            lock (_gate)
            {
                return _pass;
            }
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            AccessChange change;
            lock (_gate)
            {
                if (_pending is null || _stopping.IsCancellationRequested)
                {
                    _pending = null;
                    _running = false;
                    return;
                }

                change = _pending;
                _pending = null;
            }

            try
            {
                await RecheckAsync(change, _stopping.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Checking the live feeds' access again failed; the next access change tries again.");
            }
            catch (OperationCanceledException)
            {
                // Stopping; the loop sees it and ends.
            }
        }
    }

    internal Task RecheckAllAsync(CancellationToken cancellationToken) => RecheckAsync(AccessChange.All, cancellationToken);

    internal async Task RecheckAsync(AccessChange change, CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(
            _connections.Joined().Where(c => c.IsTouchedBy(change)),
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            async (connection, ct) =>
            {
                await using var scope = _scopes.CreateAsyncScope();
                var auth = scope.ServiceProvider.GetRequiredService<IAuthServiceClient>();
                await RecheckAsync(auth, connection, ct);
            });
    }

    private async Task RecheckAsync(IAuthServiceClient auth, LiveFeedConnection connection, CancellationToken cancellationToken)
    {
        if (connection.AccessToken is not { Length: > 0 } token)
        {
            return;
        }

        var answers = new Dictionary<Guid, Result<WorkspaceAccess>>();
        async Task<Result<WorkspaceAccess>> AccessAsync(Guid workspaceRef)
        {
            if (!answers.TryGetValue(workspaceRef, out var answer))
            {
                answer = await auth.ResolveWorkspaceForTokenAsync(token, workspaceRef, cancellationToken);
                answers[workspaceRef] = answer;
            }

            return answer;
        }

        foreach (var (workspaceRef, workspaceId) in connection.Workspaces)
        {
            if (IsRefused(await AccessAsync(workspaceRef), Permissions.WorkspaceJoin)
                && connection.Workspaces.TryRemove(workspaceRef, out _))
            {
                _logger.LogInformation("Connection {ConnectionId} may no longer read workspace {WorkspaceId}; removed from its feed.", connection.ConnectionId, workspaceId);
                await _hub.Groups.RemoveFromGroupAsync(connection.ConnectionId, NotificationHub.GroupName(workspaceId), cancellationToken);
            }
        }

        foreach (var (clientRef, watched) in connection.Clients)
        {
            if (IsRefused(await AccessAsync(watched.WorkspaceRef), Permissions.WorkspaceJoin, Permissions.LogsRead)
                && connection.Clients.TryRemove(clientRef, out _))
            {
                _logger.LogInformation("Connection {ConnectionId} may no longer read client {ClientId}; removed from its feed.", connection.ConnectionId, watched.ClientId);
                await _hub.Groups.RemoveFromGroupAsync(connection.ConnectionId, NotificationHub.ClientGroupName(watched.ClientId), cancellationToken);
            }
        }
    }

    // Only a refusal takes a feed away; an auth host that could not answer is not one.
    private static bool IsRefused(Result<WorkspaceAccess> access, params PermissionSlug[] needed) =>
        access.IsFailure
            ? access.Error.Type is ErrorType.Forbidden or ErrorType.Unauthorized
            : needed.Any(p => !access.Value.HasPermission(p));
}
