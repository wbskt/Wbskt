using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;

namespace Wbskt.Host.Tests.Abstraction;

public sealed class WakeConditionSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static TheoryData<WakeCondition, Type> RoundTripCases => new()
    {
        { new TimerWakeCondition(new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc)), typeof(TimerWakeCondition) },
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

    /// <summary>
    /// Kind and ExpiresAt are computed for the bookmark row, not stored: the serialized condition keeps
    /// the shape stored bookmarks already have, with the discriminator as the only "kind".
    /// </summary>
    [Fact]
    public void Computed_members_are_not_serialized()
    {
        WakeCondition condition = new SignalWakeCondition("operator-ack", "corr-42") { Ttl = TimeSpan.FromMinutes(1), TtlPort = "timeout" };

        string json = JsonSerializer.Serialize(condition, Options);

        Assert.Equal("""{"kind":"signal","name":"operator-ack","correlation":"corr-42","ttl":"00:01:00","ttlPort":"timeout"}""", json);
    }
}
