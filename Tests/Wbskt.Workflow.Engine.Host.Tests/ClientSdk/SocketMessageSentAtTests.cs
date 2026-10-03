using System.Text.Json;
using Wbskt.Client.Sdk.Models;
using Wbskt.Socket.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ClientSdk;

public sealed class SocketMessageSentAtTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Sent_time_round_trips_and_is_left_out_when_unset()
    {
        var sentAt = new DateTimeOffset(2026, 10, 3, 11, 59, 0, TimeSpan.Zero);
        var json = JsonSerializer.Serialize(new SocketMessage("temperature", new { celsius = 4 }) { SentAt = sentAt });

        Assert.Equal(sentAt, JsonSerializer.Deserialize<SocketMessage>(json)!.SentAt);
        Assert.DoesNotContain("sentAt", JsonSerializer.Serialize(new SocketMessage("sys.pong", new { })));
    }

    [Theory]
    [InlineData("{\"type\":\"t\",\"payload\":{}}")]
    [InlineData("{\"type\":\"t\",\"payload\":{},\"sentAt\":null}")]
    [InlineData("{\"type\":\"t\",\"payload\":{},\"sentAt\":\"yesterday-ish\"}")]
    [InlineData("{\"type\":\"t\",\"payload\":{},\"sentAt\":1759492800}")]
    [InlineData("{\"type\":\"t\",\"payload\":{},\"sentAt\":{\"at\":1}}")]
    public void A_missing_or_unreadable_sent_time_does_not_reject_the_message(string json)
    {
        var message = JsonSerializer.Deserialize<SocketMessage>(json)!;

        Assert.Equal("t", message.Type);
        Assert.Null(message.SentAt);
    }

    [Fact]
    public void A_sent_time_without_an_offset_is_read_as_utc()
    {
        var message = JsonSerializer.Deserialize<SocketMessage>("{\"type\":\"t\",\"payload\":{},\"sentAt\":\"2026-10-03T11:00:00\"}")!;

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 11, 0, 0, TimeSpan.Zero), message.SentAt);
    }

    [Fact]
    public void The_socket_host_keeps_a_plausible_device_time()
    {
        var hourAgo = new DateTimeOffset(Now.AddHours(-1));

        Assert.Equal(Now.AddHours(-1), SocketHandler.PlausibleSentAt(hourAgo, Now));
        Assert.Equal(DateTimeKind.Utc, SocketHandler.PlausibleSentAt(hourAgo, Now)!.Value.Kind);
    }

    [Fact]
    public void The_socket_host_converts_a_device_time_with_an_offset_to_utc()
    {
        var local = new DateTimeOffset(2026, 10, 3, 16, 30, 0, TimeSpan.FromHours(5.5));

        Assert.Equal(new DateTime(2026, 10, 3, 11, 0, 0, DateTimeKind.Utc), SocketHandler.PlausibleSentAt(local, Now));
    }

    [Fact]
    public void The_socket_host_treats_a_slightly_fast_device_clock_as_now()
    {
        Assert.Equal(Now, SocketHandler.PlausibleSentAt(new DateTimeOffset(Now.AddSeconds(20)), Now));
    }

    [Theory]
    [InlineData(5)]       // minutes in the future
    [InlineData(-8 * 24 * 60)] // more than a week old
    public void The_socket_host_ignores_an_implausible_device_time(int minutesFromNow)
    {
        Assert.Null(SocketHandler.PlausibleSentAt(new DateTimeOffset(Now.AddMinutes(minutesFromNow)), Now));
    }

    [Fact]
    public void The_socket_host_ignores_a_missing_device_time()
    {
        Assert.Null(SocketHandler.PlausibleSentAt(null, Now));
    }
}
