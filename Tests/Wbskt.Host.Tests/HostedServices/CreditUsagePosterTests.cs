using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Host.Tests.HostedServices;

public sealed class CreditUsagePosterTests
{
    private static readonly DateTime Now = new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Tick_posts_in_batches_on_todays_date_until_caught_up()
    {
        var provider = new RecordingCreditUsageProvider([CreditUsagePoster.BatchSize, 3]);
        var poster = new CreditUsagePoster(new FixedClock(), new FixedLeadershipGate(true), WorkerTestServices.Scopes(provider), NullLogger<CreditUsagePoster>.Instance, WorkerTestServices.Options());

        await poster.TickAsync(CancellationToken.None);

        // Runs that ended within the last hour (the default window) are still read.
        Assert.Equal(2, provider.Calls.Count);
        Assert.All(provider.Calls, call => Assert.Equal((new DateOnly(2026, 5, 26), Now.AddHours(-1), CreditUsagePoster.BatchSize), call));
    }

    [Fact]
    public async Task A_standby_posts_nothing()
    {
        var provider = new RecordingCreditUsageProvider([0]);
        var poster = new CreditUsagePoster(new FixedClock(), new FixedLeadershipGate(false), WorkerTestServices.Scopes(provider), NullLogger<CreditUsagePoster>.Instance, WorkerTestServices.Options());

        await poster.TickAsync(CancellationToken.None);

        Assert.Empty(provider.Calls);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedLeadershipGate(bool isActive) : ILeadershipGate
    {
        public bool IsActive => isActive;
    }

    private sealed class RecordingCreditUsageProvider(IReadOnlyCollection<int> posts) : ICreditUsageProvider
    {
        private readonly Queue<int> _posts = new(posts);

        public List<(DateOnly Day, DateTime EndedSince, int BatchSize)> Calls { get; } = [];

        public Task<int> PostAsync(DateOnly day, DateTime endedSinceUtc, int batchSize, CancellationToken ct)
        {
            Calls.Add((day, endedSinceUtc, batchSize));
            return Task.FromResult(_posts.Dequeue());
        }
    }
}
