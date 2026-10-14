using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.Runtime;

/// <summary>
/// The engine's trigger registration cache: what it serves, what it reads again, and that a change
/// announcement (<see cref="TriggerRegistrationCache.Clear"/>) drops every entry, "nothing listens"
/// included.
/// </summary>
public sealed class TriggerRegistrationCacheTests
{
    private const string Channel = "client";
    private const string Key = "client:serial-1:telemetry";

    [Fact]
    public async Task A_miss_reads_and_keeps_the_answer()
    {
        var source = new CountingSource(Registration(7));
        using var cache = CreateCache();

        var registrations = await cache.GetOrLoadAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        Assert.Equal(7, Assert.Single(registrations).Id);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task A_hit_is_served_without_a_read()
    {
        var source = new CountingSource(Registration(7));
        using var cache = CreateCache();

        var first = await cache.GetOrLoadAsync(Channel, Key, source.LoadAsync, CancellationToken.None);
        var second = await cache.GetOrLoadAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task Entries_are_per_channel_and_key()
    {
        var source = new CountingSource();
        using var cache = CreateCache();

        await cache.GetOrLoadAsync(Channel, Key, source.LoadAsync, CancellationToken.None);
        await cache.GetOrLoadAsync(Channel, "client:serial-2:telemetry", source.LoadAsync, CancellationToken.None);
        await cache.GetOrLoadAsync("client-hold", Key, source.LoadAsync, CancellationToken.None);

        Assert.Equal(3, source.Reads);
    }

    [Fact]
    public async Task Clear_evicts_an_entry_so_the_next_lookup_reads_again()
    {
        var source = new CountingSource(Registration(7));
        using var cache = CreateCache();
        await cache.GetOrLoadAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        source.Rows = [Registration(8)];
        cache.Clear();
        var registrations = await cache.GetOrLoadAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        Assert.Equal(8, Assert.Single(registrations).Id);
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task An_empty_answer_is_kept_and_served()
    {
        var source = new CountingSource();
        using var cache = CreateCache();

        await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);
        var registrations = await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        Assert.Empty(registrations);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task Clear_evicts_an_empty_answer_so_a_newly_published_trigger_is_seen()
    {
        // The case that matters: the key had no registrations, a workflow was then published on it, and
        // the announcement arrived. Serving the old "nothing listens" would drop the device's messages.
        var source = new CountingSource();
        using var cache = CreateCache();
        await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        source.Rows = [Registration(9)];
        cache.Clear();
        var registrations = await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        Assert.Equal(9, Assert.Single(registrations).Id);
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task LoadUnlessKnownEmpty_reads_again_whenever_registrations_are_cached()
    {
        // The dispatcher starts runs from what it gets back, so a cached registration is never acted on
        // without the database confirming it: removed since, it is not returned.
        var source = new CountingSource(Registration(7));
        using var cache = CreateCache();
        await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        source.Rows = [];
        var registrations = await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        Assert.Empty(registrations);
        Assert.Equal(2, source.Reads);

        // ...and the fresh empty answer is what is kept.
        await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task A_read_under_way_when_a_change_is_announced_is_not_kept()
    {
        // The read saw the registrations before the change; storing it after the clear would bring the
        // stale answer back.
        var source = new CountingSource();
        using var cache = CreateCache();
        source.DuringRead = cache.Clear;

        await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);
        source.DuringRead = null;
        await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task An_entry_expires_after_its_lifetime_even_without_an_announcement()
    {
        // The backstop for an announcement that is late or lost.
        var clock = new ManualClock();
        var source = new CountingSource();
        using var cache = CreateCache(TimeSpan.FromSeconds(60), clock);
        await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(59));
        await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);
        Assert.Equal(1, source.Reads);

        clock.Advance(TimeSpan.FromSeconds(1));
        await cache.LoadUnlessKnownEmptyAsync(Channel, Key, source.LoadAsync, CancellationToken.None);
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task A_lifetime_of_zero_turns_the_cache_off()
    {
        var source = new CountingSource();
        using var cache = CreateCache(TimeSpan.Zero);

        await cache.GetOrLoadAsync(Channel, Key, source.LoadAsync, CancellationToken.None);
        await cache.GetOrLoadAsync(Channel, Key, source.LoadAsync, CancellationToken.None);

        Assert.Equal(2, source.Reads);
    }

    internal static TriggerRegistrationCache CreateCache(TimeSpan? lifetime = null, TimeProvider? clock = null) =>
        new(Options.Create(new WorkflowEngineOptions { TriggerRegistrationCacheLifetime = lifetime ?? TimeSpan.FromMinutes(1) }), clock ?? TimeProvider.System);

    internal static TriggerRegistrationRow Registration(int id, string triggerKind = Channel, string triggerKey = Key) => new()
    {
        Id = id,
        WorkflowDefinitionId = 100 + id,
        WorkflowRefId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        WorkflowVersion = 1,
        TriggerNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        TriggerKind = triggerKind,
        TriggerKey = triggerKey,
        CorrelationExpression = null,
        ConcurrencyPolicy = "AllowParallel",
        FilterExpression = null,
        CreatedAt = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>Stands in for the database read, counting how often it is asked.</summary>
    private sealed class CountingSource(params TriggerRegistrationRow[] rows)
    {
        public TriggerRegistrationRow[] Rows { get; set; } = rows;

        public int Reads { get; private set; }

        public Action? DuringRead { get; set; }

        public Task<IReadOnlyCollection<TriggerRegistrationRow>> LoadAsync(CancellationToken ct)
        {
            Reads++;
            IReadOnlyCollection<TriggerRegistrationRow> answer = Rows.ToArray();
            DuringRead?.Invoke();
            return Task.FromResult(answer);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
