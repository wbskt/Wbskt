using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentTime_WithinOneSecondTolerance()
    {
        // Arrange
        var clock = new SystemClock();
        var before = DateTime.UtcNow;

        // Act
        var result = clock.UtcNow;
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(result >= before, "Clock should not return time before call");
        Assert.True(result <= after, "Clock should not return time after call");
        Assert.True((after - before).TotalSeconds < 1, "Test should complete within 1 second");
    }

    [Fact]
    public void UtcNow_ReturnsUtcKind()
    {
        // Arrange
        var clock = new SystemClock();

        // Act
        var result = clock.UtcNow;

        // Assert
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void UtcNow_IsConsistentWithSystemUtcNow()
    {
        // Arrange
        var clock = new SystemClock();
        var systemNow = DateTime.UtcNow;

        // Act
        var clockNow = clock.UtcNow;

        // Assert - within 100ms tolerance
        var diff = Math.Abs((clockNow - systemNow).TotalMilliseconds);
        Assert.True(diff < 100, $"Clock drift should be less than 100ms, was {diff}ms");
    }
}
