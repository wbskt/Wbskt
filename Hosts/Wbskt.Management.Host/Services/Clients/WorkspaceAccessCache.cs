using Microsoft.Extensions.Caching.Memory;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>
/// Remembers a user's resolved access to a workspace for a short while, so a console page that fires
/// several API calls asks the auth host once rather than once per call.
/// </summary>
/// <remarks>
/// Only successful resolutions are kept. A denial is always re-checked, so someone who has just been
/// given access does not wait for an entry to expire. Removed access (a role, a grant, membership, a
/// suspension) is the other direction: the auth host announces every such change on Redis
/// (<see cref="WorkspaceAccessChanges"/>) and the whole cache is dropped when one arrives, or when the
/// Redis connection comes back after missing some. <c>WorkspaceAccessCache:Seconds</c> bounds how long
/// a change can go unnoticed when Redis is not configured or down. Signing out or a password change is
/// not affected either way, because the token itself is checked against the revocation list on every
/// request before anything reaches this cache. Set the value to 0 to turn caching off.
/// </remarks>
internal sealed class WorkspaceAccessCache : IHostedService, IDisposable
{
    public const string SecondsKey = "WorkspaceAccessCache:Seconds";
    private const int DefaultSeconds = 30;

    // Each entry is a workspace id and a few permission slugs, so this caps the cache at a few MB.
    private const int MaxEntries = 10_000;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });
    private readonly TimeSpan _lifetime;
    private readonly WorkspaceAccessChanges? _changes;
    private long _generation;

    public WorkspaceAccessCache(IConfiguration configuration, WorkspaceAccessChanges? changes = null)
    {
        var seconds = configuration.GetValue(SecondsKey, DefaultSeconds);
        _lifetime = TimeSpan.FromSeconds(Math.Max(0, seconds));
        _changes = changes;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_changes is not null && _lifetime > TimeSpan.Zero)
        {
            await _changes.SubscribeAsync(Clear);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
        return _lifetime > TimeSpan.Zero && _cache.TryGetValue((userId, workspaceRef), out access);
    }

    public void Set(int userId, Guid workspaceRef, WorkspaceAccess access, long generation)
    {
        if (_lifetime <= TimeSpan.Zero || generation != Generation)
        {
            return;
        }

        _cache.Set((userId, workspaceRef), access, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _lifetime,
            Size = 1
        });
    }

    public void Dispose() => _cache.Dispose();
}
