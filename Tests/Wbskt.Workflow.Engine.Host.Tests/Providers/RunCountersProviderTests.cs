using FluentAssertions;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class RunCountersProviderTests
{
    [Fact]
    public void RunCountersProvider_exists()
    {
        var type = typeof(RunCountersProvider);
        type.Should().NotBeNull();
    }
}
