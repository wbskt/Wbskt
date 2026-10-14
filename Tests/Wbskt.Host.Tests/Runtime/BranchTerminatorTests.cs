using Moq;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.Runtime;

public sealed class BranchTerminatorTests
{
    [Fact]
    public async Task Releasing_the_last_branch_finalizes_the_run()
    {
        var counters = new Mock<IRunCountersProvider>();
        counters.Setup(c => c.DecrementActiveBranchesAsync(42, 1, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var finalizer = new Mock<IRunFinalizer>();
        var terminator = new BranchTerminator(counters.Object, new Lazy<IRunFinalizer>(() => finalizer.Object));

        int remaining = await terminator.ReleaseAsync(42, 1, CancellationToken.None);

        Assert.Equal(0, remaining);
        finalizer.Verify(f => f.FinalizeAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Releasing_a_branch_while_others_run_leaves_the_run_alone_and_never_builds_the_finalizer()
    {
        var counters = new Mock<IRunCountersProvider>();
        counters.Setup(c => c.DecrementActiveBranchesAsync(42, 1, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        bool finalizerBuilt = false;
        var terminator = new BranchTerminator(counters.Object, new Lazy<IRunFinalizer>(() =>
        {
            finalizerBuilt = true;
            return Mock.Of<IRunFinalizer>();
        }));

        int remaining = await terminator.ReleaseAsync(42, 1, CancellationToken.None);

        Assert.Equal(2, remaining);
        Assert.False(finalizerBuilt);
    }

    [Fact]
    public async Task Releasing_uses_the_clamped_decrement_and_never_the_unclamped_increment()
    {
        // The increment procedure has no floor, so a replayed "-1" could drive the count negative and
        // the run would never again look idle. Every release goes through the clamped decrement.
        var counters = new Mock<IRunCountersProvider>(MockBehavior.Strict);
        counters.Setup(c => c.DecrementActiveBranchesAsync(42, 3, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var terminator = new BranchTerminator(counters.Object, new Lazy<IRunFinalizer>(Mock.Of<IRunFinalizer>));

        await terminator.ReleaseAsync(42, 3, CancellationToken.None);

        counters.Verify(c => c.DecrementActiveBranchesAsync(42, 3, It.IsAny<CancellationToken>()), Times.Once);
    }
}
