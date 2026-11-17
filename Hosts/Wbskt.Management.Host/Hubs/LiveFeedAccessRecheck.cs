using Microsoft.AspNetCore.SignalR;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Models;

namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// Takes a console connection out of the feeds it may no longer read. Access is checked when a
/// connection joins a workspace or client feed; without this, someone removed from the workspace or
/// suspended in its tenant kept receiving its events until their token expired, while every HTTP call
/// of theirs was already refused. Whenever access may have changed (the auth host's announcement, which
/// also empties <see cref="WorkspaceAccessCache"/>), each joined feed is resolved again with the token
/// its connection opened with, and a feed the connection is now refused is left for it.
/// </summary>
/// <remarks>
/// Changes are rare administrative actions, so every feed is checked rather than working out whose
/// access moved. Announcements that arrive during a pass are folded into one more pass. An auth host
/// that does not answer leaves the feeds as they are: the token's expiry still bounds them.
/// </remarks>
internal sealed class LiveFeedAccessRecheck : IHostedService
{
    private const int Parallelism = 8;

    private readonly WorkspaceAccessCache _cache;
    private readonly LiveFeedConnections _connections;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LiveFeedAccessRecheck> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private int _requested;
    private Task _pass = Task.CompletedTask;

    public LiveFeedAccessRecheck(
        WorkspaceAccessCache cache,
        LiveFeedConnections connections,
        IHubContext<NotificationHub> hub,
        IServiceScopeFactory scopes,
        ILogger<LiveFeedAccessRecheck> logger)
    {
        _cache = cache;
        _connections = connections;
        _hub = hub;
        _scopes = scopes;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cache.Cleared += Request;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cache.Cleared -= Request;
        await _stopping.CancelAsync();
        await _pass.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    /// <summary>Asks for a pass; one already running takes the request as one more pass after it.</summary>
    internal void Request()
    {
        if (Interlocked.Increment(ref _requested) == 1)
        {
            _pass = Task.Run(RunAsync);
        }
    }

    /// <summary>The pass in flight, for a test to wait on.</summary>
    internal Task Pass => _pass;

    private async Task RunAsync()
    {
        do
        {
            Volatile.Write(ref _requested, 1);
            try
            {
                await RecheckAllAsync(_stopping.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Checking the live feeds' access again failed; the next access change tries again.");
            }
        }
        while (Interlocked.CompareExchange(ref _requested, 0, 1) != 1 && !_stopping.IsCancellationRequested);
    }

    internal async Task RecheckAllAsync(CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(
            _connections.Joined(),
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
