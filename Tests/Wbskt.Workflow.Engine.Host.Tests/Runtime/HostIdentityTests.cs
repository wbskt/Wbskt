using System.Diagnostics;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class HostIdentityTests
{
    [Fact]
    public void HostId_includes_machine_and_process()
    {
        // Arrange
        var identity = new HostIdentity();
        string[] parts = identity.HostId.Split(':');

        // Assert
        Assert.Equal(3, parts.Length);
        Assert.Equal(Environment.MachineName, parts[0]);
        Assert.Equal(Environment.ProcessId.ToString(), parts[1]);
        Assert.True(Guid.TryParseExact(parts[2], "N", out _));
    }

    [Fact]
    public void Two_instances_differ()
    {
        // Arrange
        var first = new HostIdentity();
        var second = new HostIdentity();

        // Assert
        Assert.NotEqual(first.HostId, second.HostId);
    }
}
