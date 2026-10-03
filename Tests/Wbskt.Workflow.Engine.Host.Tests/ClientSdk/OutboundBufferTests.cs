using Wbskt.Client.Sdk.Internal;
using Wbskt.Client.Sdk.Models;

namespace Wbskt.Workflow.Engine.Host.Tests.ClientSdk;

public sealed class OutboundBufferTests
{
    [Fact]
    public void Messages_come_out_in_the_order_they_were_queued()
    {
        var buffer = new OutboundBuffer(10);
        buffer.Enqueue(Message("a"));
        buffer.Enqueue(Message("b"));

        Assert.Equal(["a", "b"], Drain(buffer));
    }

    [Fact]
    public void A_full_buffer_drops_the_oldest_message()
    {
        var buffer = new OutboundBuffer(2);
        buffer.Enqueue(Message("a"));
        buffer.Enqueue(Message("b"));
        buffer.Enqueue(Message("c"));

        Assert.Equal(1, buffer.DroppedCount);
        Assert.Equal(["b", "c"], Drain(buffer));
    }

    [Fact]
    public void A_message_dropped_while_it_was_being_sent_does_not_take_the_next_one_with_it()
    {
        var buffer = new OutboundBuffer(1);
        buffer.Enqueue(Message("a"));
        Assert.True(buffer.TryPeek(out var sending));

        // Overflows while "a" is on the wire, so "a" is no longer the head.
        buffer.Enqueue(Message("b"));
        buffer.RemoveIfHead(sending);

        Assert.Equal(["b"], Drain(buffer));
    }

    [Fact]
    public void A_zero_capacity_buffer_keeps_nothing()
    {
        var buffer = new OutboundBuffer(0);
        buffer.Enqueue(Message("a"));

        Assert.Equal(0, buffer.Count);
        Assert.False(buffer.TryPeek(out _));
    }

    private static SocketMessage Message(string type) => new(type, new { });

    private static List<string> Drain(OutboundBuffer buffer)
    {
        var types = new List<string>();
        while (buffer.TryPeek(out var next))
        {
            types.Add(next.Type);
            buffer.RemoveIfHead(next);
        }

        return types;
    }
}
