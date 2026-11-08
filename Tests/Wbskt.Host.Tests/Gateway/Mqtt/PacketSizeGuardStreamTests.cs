using Wbskt.Gateway.MQTT.Host.Infrastructure;

namespace Wbskt.Host.Tests.Gateway.Mqtt;

public sealed class PacketSizeGuardStreamTests
{
    [Fact]
    public async Task Packets_at_the_limit_pass_even_read_a_byte_at_a_time()
    {
        // Two packets back to back: 127 bytes (one length byte) and 200 bytes (two length bytes).
        byte[] bytes = [0x30, 0x7F, .. new byte[127], 0x30, 0xC8, 0x01, .. new byte[200]];
        using var guard = new PacketSizeGuardStream(new MemoryStream(bytes), maxPacketBytes: 200);

        var total = 0;
        var buffer = new byte[1];
        int read;
        while ((read = await guard.ReadAsync(buffer)) > 0)
        {
            total += read;
        }

        Assert.Equal(bytes.Length, total);
        Assert.False(guard.Exceeded);
    }

    [Fact]
    public async Task A_packet_over_the_limit_fails_the_read_that_brings_its_header()
    {
        byte[] bytes = [0x30, 0xC9, 0x01, .. new byte[201]];
        using var guard = new PacketSizeGuardStream(new MemoryStream(bytes), maxPacketBytes: 200);

        await Assert.ThrowsAsync<IOException>(() => guard.ReadAsync(new byte[3]).AsTask());

        Assert.True(guard.Exceeded);
    }

    [Fact]
    public async Task A_remaining_length_longer_than_four_bytes_is_malformed()
    {
        using var guard = new PacketSizeGuardStream(new MemoryStream([0x10, 0xFF, 0xFF, 0xFF, 0xFF, 0x01]), maxPacketBytes: int.MaxValue);

        await Assert.ThrowsAsync<IOException>(() => guard.ReadAsync(new byte[6]).AsTask());

        Assert.False(guard.Exceeded);
    }

    [Fact]
    public async Task A_packet_with_no_body_is_followed_by_the_next_header()
    {
        // PINGREQ (no body), then a 300-byte PUBLISH over a limit of 256.
        using var guard = new PacketSizeGuardStream(new MemoryStream([0xC0, 0x00, 0x30, 0xAC, 0x02]), maxPacketBytes: 256);

        await Assert.ThrowsAsync<IOException>(() => guard.ReadAsync(new byte[5]).AsTask());
    }
}
