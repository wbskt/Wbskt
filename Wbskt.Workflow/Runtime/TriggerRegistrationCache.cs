using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// The engine's <see cref="ITriggerRegistrationCache"/>: the same generation-stamped shape as the
/// management host's <c>WorkspaceAccessCache</c>, cleared by a <c>WorkflowDefinitionChangedEvent</c>
/// rather than a Redis announcement.
/// </summary>
/// <remarks>
/// Every entry is stamped with the generation that was current when its read <em>began</em>, and is
/// served only while that is still the current one. <see cref="Clear"/> moves the generation on, so a
/// read that started before a change and finishes after it is never served, even if it is stored after
/// the clear. Entries also expire <see cref="WorkflowEngineOptions.TriggerRegistrationCacheLifetime"/>
/// after they were read, which bounds how long a lost announcement can go unnoticed. A lifetime of zero
/// turns the cache off.
/// <para>
/// What the cache may get wrong is bounded the same way for every caller: an empty entry for a key a
/// workflow has just started listening on, for as long as the announcement takes to arrive (or the
/// lifetime, if it never does). The trigger dispatcher reads a key again whenever the cache holds
/// registrations for it (<see cref="LoadUnlessKnownEmptyAsync"/>), so a stale entry can make it read
/// the database once more, never claim an event or start a run for a registration that is gone.
/// </para>
/// </remarks>
internal sealed class TriggerRegistrationCache : ITriggerRegistrationCache, IDisposable
{
    // Each entry is a key and, for most, an empty list; this caps the cache at a few tens of MB.
    internal const int MaxEntries = 50_000;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });
    private readonly TimeSpan _lifetime;
    private readonly TimeProvider _clock;
    private long _generation;

    public TriggerRegistrationCache(IOptions<WorkflowEngineOptions> options, TimeProvider clock)
    {
        TimeSpan lifetime = options.Value.TriggerRegistrationCacheLifetime;
        _lifetime = lifetime > TimeSpan.Zero ? lifetime : TimeSpan.Zero;
        _clock = clock;
    }

    private bool Enabled => _lifetime > TimeSpan.Zero;

    /// <summary>Moves on with every <see cref="Clear"/>; an entry stamped with an earlier one is never served.</summary>
    internal long Generation => Interlocked.Read(ref _generation);

    public async Task<IReadOnlyCollection<TriggerRegistrationRow>> GetOrLoadAsync(
        string channelKind,
        string key,
        Func<CancellationToken, Task<IReadOnlyCollection<TriggerRegistrationRow>>> load,
        CancellationToken ct)
    {
        if (TryGet(channelKind, key, out IReadOnlyCollection<TriggerRegistrationRow>? cached))
        {
            return cached;
        }

        return await LoadAndKeepAsync(channelKind, key, load, ct);
    }

    public async Task<IReadOnlyCollection<TriggerRegistrationRow>> LoadUnlessKnownEmptyAsync(
        string channelKind,
        string key,
        Func<CancellationToken, Task<IReadOnlyCollection<TriggerRegistrationRow>>> load,
        CancellationToken ct)
    {
        if (TryGet(channelKind, key, out IReadOnlyCollection<TriggerRegistrationRow>? cached) && cached.Count == 0)
        {
            return cached;
        }

        return await LoadAndKeepAsync(channelKind, key, load, ct);
    }

    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _cache.Clear();
    }

    private async Task<IReadOnlyCollection<TriggerRegistrationRow>> LoadAndKeepAsync(
        string channelKind,
        string key,
        Func<CancellationToken, Task<IReadOnlyCollection<TriggerRegistrationRow>>> load,
        CancellationToken ct)
    {
        // Read before the load: a change announced while it runs moves the generation past this one,
        // so what the load returns is kept under a generation that is no longer served.
        long generation = Generation;
        DateTimeOffset readAt = _clock.GetUtcNow();
        IReadOnlyCollection<TriggerRegistrationRow> registrations = await load(ct);

        if (Enabled && generation == Generation)
        {
            _cache.Set(new CacheKey(channelKind, key), new Entry(registrations, generation, readAt), new MemoryCacheEntryOptions
            {
                // A backstop for memory only; how long an entry is served is checked against the clock below.
                AbsoluteExpirationRelativeToNow = _lifetime,
                Size = 1
            });
        }

        return registrations;
    }

    private bool TryGet(string channelKind, string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IReadOnlyCollection<TriggerRegistrationRow>? registrations)
    {
        registrations = null;
        if (!Enabled || !_cache.TryGetValue(new CacheKey(channelKind, key), out Entry? entry) || entry is null)
        {
            return false;
        }

        if (entry.Generation != Generation || _clock.GetUtcNow() - entry.ReadAt >= _lifetime)
        {
            return false;
        }

        registrations = entry.Registrations;
        return true;
    }

    public void Dispose() => _cache.Dispose();

    private readonly record struct CacheKey(string ChannelKind, string Key);

    private sealed record Entry(IReadOnlyCollection<TriggerRegistrationRow> Registrations, long Generation, DateTimeOffset ReadAt);
}
