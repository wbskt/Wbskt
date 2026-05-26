using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public sealed class WakeConditionSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static TheoryData<WakeCondition, Type> RoundTripCases => new()
    {
        { new TimerWakeCondition(new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)), typeof(TimerWakeCondition) },
        { new InboundWakeCondition("device-1", "temperature"), typeof(InboundWakeCondition) },
        { new SignalWakeCondition("operator-ack", "corr-42"), typeof(SignalWakeCondition) },
        { new ChildRunCompletedWakeCondition(Guid.Parse("11111111-1111-1111-1111-111111111111")), typeof(ChildRunCompletedWakeCondition) }
    };

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Each_wake_condition_subtype_round_trips(WakeCondition condition, Type expectedType)
    {
        // Arrange
        string json = JsonSerializer.Serialize(condition, Options);

        // Act
        WakeCondition? roundTripped = JsonSerializer.Deserialize<WakeCondition>(json, Options);

        // Assert
        Assert.NotNull(roundTripped);
        Assert.IsType(expectedType, roundTripped);
    }
}
