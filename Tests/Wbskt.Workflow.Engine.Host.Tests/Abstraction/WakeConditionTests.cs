using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class WakeConditionTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TimerWakeCondition_roundtrips()
    {
        WakeCondition cond = new TimerWakeCondition(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var json = JsonSerializer.Serialize(cond, Options);
        var back = JsonSerializer.Deserialize<WakeCondition>(json, Options);
        Assert.IsType<TimerWakeCondition>(back);
    }

    [Fact]
    public void SignalWakeCondition_roundtrips()
    {
        WakeCondition cond = new SignalWakeCondition("operator-ack", "R-42");
        var json = JsonSerializer.Serialize(cond, Options);
        var back = JsonSerializer.Deserialize<WakeCondition>(json, Options);
        var s = Assert.IsType<SignalWakeCondition>(back);
        Assert.Equal("operator-ack", s.Name);
        Assert.Equal("R-42", s.Correlation);
    }

    [Fact]
    public void AnyOfWakeCondition_roundtrips_nested_polymorphic_conditions()
    {
        WakeCondition cond = new AnyOfWakeCondition([
            new TimerWakeCondition(DateTime.UtcNow),
            new SignalWakeCondition("ack", "r1")
        ]);
        var json = JsonSerializer.Serialize(cond, Options);
        var back = JsonSerializer.Deserialize<WakeCondition>(json, Options);
        var anyOf = Assert.IsType<AnyOfWakeCondition>(back);
        Assert.Equal(2, anyOf.Conditions.Count);
    }

    [Fact]
    public void InboundWakeCondition_roundtrips()
    {
        WakeCondition cond = new InboundWakeCondition("dev-A", "temperature");
        var json = JsonSerializer.Serialize(cond, Options);
        var back = JsonSerializer.Deserialize<WakeCondition>(json, Options);
        Assert.IsType<InboundWakeCondition>(back);
    }

    [Fact]
    public void HttpWakeCondition_roundtrips()
    {
        WakeCondition cond = new HttpWakeCondition("tok-123");
        var json = JsonSerializer.Serialize(cond, Options);
        var back = JsonSerializer.Deserialize<WakeCondition>(json, Options);
        Assert.IsType<HttpWakeCondition>(back);
    }

    [Fact]
    public void ChildRunCompletedWakeCondition_roundtrips()
    {
        WakeCondition cond = new ChildRunCompletedWakeCondition(Guid.NewGuid());
        var json = JsonSerializer.Serialize(cond, Options);
        var back = JsonSerializer.Deserialize<WakeCondition>(json, Options);
        Assert.IsType<ChildRunCompletedWakeCondition>(back);
    }
}
