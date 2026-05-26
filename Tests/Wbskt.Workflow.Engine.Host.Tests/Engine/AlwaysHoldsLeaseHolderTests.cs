using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Engine;

namespace Wbskt.Workflow.Engine.Host.Tests.Engine;

public sealed class AlwaysHoldsLeaseHolderTests
{
    [Fact]
    public async Task Acquire_always_returns_true()
    {
        // Arrange
        ILeaseHolder leaseHolder = new AlwaysHoldsLeaseHolder();

        // Act
        bool acquired = await leaseHolder.TryAcquireAsync("schedule-tick", CancellationToken.None);

        // Assert
        Assert.True(acquired);
    }

    [Fact]
    public async Task IsHeld_always_returns_true()
    {
        // Arrange
        ILeaseHolder leaseHolder = new AlwaysHoldsLeaseHolder();

        // Act
        bool isHeld = await leaseHolder.IsHeldAsync("schedule-tick", CancellationToken.None);

        // Assert
        Assert.True(isHeld);
    }

    [Fact]
    public async Task Release_does_not_throw()
    {
        // Arrange
        ILeaseHolder leaseHolder = new AlwaysHoldsLeaseHolder();

        // Act
        Task release = leaseHolder.ReleaseAsync("schedule-tick", CancellationToken.None);

        // Assert
        await release;
    }
}
