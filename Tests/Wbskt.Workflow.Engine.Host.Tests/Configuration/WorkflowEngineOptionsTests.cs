using Microsoft.Extensions.Configuration;
using Wbskt.Workflow.Abstraction.Configuration;

namespace Wbskt.Workflow.Engine.Host.Tests.Configuration;

public sealed class WorkflowEngineOptionsTests
{
    [Fact]
    public void Options_loaded_from_config_section()
    {
        // Arrange
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkflowEngine:BookmarkPollInterval"] = "00:00:02",
                ["WorkflowEngine:RunStuckThreshold"] = "00:45:00",
                ["WorkflowEngine:LeaseDurationSeconds"] = "180",
                ["WorkflowEngine:ScheduledFireLeaseBatchSize"] = "32"
            })
            .Build();

        // Act
        WorkflowEngineOptions options = configuration.GetSection("WorkflowEngine").Get<WorkflowEngineOptions>()!;

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(2), options.BookmarkPollInterval);
        Assert.Equal(TimeSpan.FromMinutes(45), options.RunStuckThreshold);
        Assert.Equal(180, options.LeaseDurationSeconds);
        Assert.Equal(32, options.ScheduledFireLeaseBatchSize);
    }
}
