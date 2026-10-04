using Microsoft.Extensions.Caching.Memory;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>
/// Remembers a user's resolved access to a workspace for a while, so a console page that fires
/// several API calls asks the auth host once rather than once per call.
/// </summary>
/// <remarks>
/// Only successful resolutions are kept. A denial is always re-checked, so someone who has just been
/// given access does not wait for an entry to expire. Removed access (a role, a grant, membership, a
/// suspension) is the other direction: the auth host announces every such change on Redis
/// (<see cref="WorkspaceAccessChanges"/>) and the whole cache is dropped when one arrives, or when the
/// Redis connection comes back after missing some.
/// <para>
/// Every <c>WorkspaceAccessCache:CheckSeconds</c> the cache also reads the change counter the auth
/// host bumps, and drops everything when it moved, which catches an announcement whose message was
/// lost. While those reads keep succeeding, an entry is served for up to
/// <c>WorkspaceAccessCache:ConfirmedSeconds</c>. When they stop (Redis not configured or down), only
/// entries younger than <c>WorkspaceAccessCache:Seconds</c> are served, which bounds how long a change
/// can go unnoticed. Signing out or a password change is not affected either way, because the token
/// itself is checked against the revocation list on every request before anything reaches this cache.
/// Set <c>Seconds</c> to 0 to turn caching off.
/// </para>
/// </remarks>
internal sealed class WorkspaceAccessCache : IHostedService, IDisposable
{
    public const string SecondsKey = "WorkspaceAccessCache:Seconds";
    public const string ConfirmedSecondsKey = "WorkspaceAccessCache:ConfirmedSeconds";
    public const string CheckSecondsKey = "WorkspaceAccessCache:CheckSeconds";
    private const int DefaultSeconds = 30;
    private const int DefaultConfirmedSeconds = 300;
    private const int DefaultCheckSeconds = 5;

    // Each entry is a workspace id and a few permission slugs, so this caps the cache at a few MB.
    private const int MaxEntries = 10_000;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _confirmedLifetime;
    private readonly TimeSpan _checkInterval;
    private readonly WorkspaceAccessChanges? _changes;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stopping = new();
    private long _generation;
    private long? _version;
    private long _confirmedAtTicks = long.MinValue;
    private Task? _checking;

    public WorkspaceAccessCache(IConfiguration configuration, WorkspaceAccessChanges? changes = null, TimeProvider? time = null)
    {
        _lifetime = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue(SecondsKey, DefaultSeconds)));
        _confirmedLifetime = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue(ConfirmedSecondsKey, DefaultConfirmedSeconds)));
        _checkInterval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue(CheckSecondsKey, DefaultCheckSeconds)));
        _changes = changes;
        _time = time ?? TimeProvider.System;
    }

    private bool Enabled => _lifetime > TimeSpan.Zero;

    private TimeSpan StoredLifetime => _changes is null ? _lifetime : TimeSpan.FromTicks(Math.Max(_lifetime.Ticks, _confirmedLifetime.Ticks));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_changes is null || !Enabled)
        {
            return;
        }

        await _changes.SubscribeAsync(Clear);
        _checking = CheckLoopAsync(_stopping.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_checking is not null)
        {
            await _checking.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>
    /// Changes on every <see cref="Clear"/>. A caller reads it before asking the auth host and hands it
    /// back to <see cref="Set"/>, so an answer that was already on its way when a change was announced
    /// is not stored after the clear meant to remove it.
    /// </summary>
    public long Generation => Interlocked.Read(ref _generation);

    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _cache.Clear();
    }

    public bool TryGet(int userId, Guid workspaceRef, out WorkspaceAccess? access)
    {
        access = null;
        if (!Enabled || !_cache.TryGetValue((userId, workspaceRef), out Entry? entry) || entry is null)
        {
            return false;
        }

        // Past the short lifetime an entry is only as good as the last confirmation that nothing changed.
        if (_time.GetUtcNow() - entry.CachedAt > _lifetime && !IsConfirmed())
        {
            return false;
        }

        access = entry.Access;
        return true;
    }

    public void Set(int userId, Guid workspaceRef, WorkspaceAccess access, long generation)
    {
        if (!Enabled || generation != Generation)
        {
            return;
        }

        _cache.Set((userId, workspaceRef), new Entry(access, _time.GetUtcNow()), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = StoredLifetime,
            Size = 1
        });
    }

    /// <summary>
    /// Reads the change counter once. A counter that moved since the last read means a change this
    /// cache may not have heard about, so everything is dropped. So is everything on the first read,
    /// since entries stored before it were never compared against any version.
    /// </summary>
    internal async Task CheckVersionAsync()
    {
        if (_changes is null)
        {
            return;
        }

        var version = await _changes.ReadVersionAsync();
        if (version is null)
        {
            return;
        }

        if (version != _version)
        {
            Clear();
            _version = version;
        }

        Interlocked.Exchange(ref _confirmedAtTicks, _time.GetUtcNow().UtcTicks);
    }

    private bool IsConfirmed()
    {
        var confirmedAt = Interlocked.Read(ref _confirmedAtTicks);
        // Two missed checks in a row and the confirmation lapses.
        return confirmedAt != long.MinValue && _time.GetUtcNow().UtcTicks - confirmedAt <= (_checkInterval * 2).Ticks;
    }

    private async Task CheckLoopAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(_checkInterval, _time);
        try
        {
            do
            {
                try
                {
                    await CheckVersionAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // ReadVersionAsync already swallows Redis errors; anything else only lapses the confirmation.
                }
            }
            while (await timer.WaitForNextTickAsync(stopping));
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
        _cache.Dispose();
    }

    private sealed record Entry(WorkspaceAccess Access, DateTimeOffset CachedAt);
}
