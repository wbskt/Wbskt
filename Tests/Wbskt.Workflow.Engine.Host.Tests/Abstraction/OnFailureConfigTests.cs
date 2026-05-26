using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Enums;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class OnFailureConfigTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void OnFailureConfig_deserialises_FailRun_outcome()
    {
        var json = """{ "outcome": "FailRun" }""";
        var config = JsonSerializer.Deserialize<OnFailureConfig>(json, Options);
        Assert.NotNull(config);
        Assert.Equal(ErrorOutcome.FailRun, config!.Outcome);
        Assert.Null(config.Compensate);
    }

    [Fact]
    public void OnFailureConfig_roundtrips_with_compensation()
    {
        var config = new OnFailureConfig(
            ErrorOutcome.Compensate,
            new CompensationDeclaration("n-refund", "action:command", null));

        var json = JsonSerializer.Serialize(config, Options);
        var back = JsonSerializer.Deserialize<OnFailureConfig>(json, Options);

        Assert.Equal(ErrorOutcome.Compensate, back!.Outcome);
        Assert.NotNull(back.Compensate);
        Assert.Equal("n-refund", back.Compensate!.NodeId);
    }
}
