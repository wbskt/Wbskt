namespace Wbskt.Host.Tests.Architecture;

/// <summary>
/// An inbound event's channel, match keys, event id and payload fields are one contract with the
/// channel that correlates it, so each channel builds its own events (<c>XChannel.Event(...)</c>). The
/// presence and hold tickers once assembled theirs by hand, which left the field names the channel's
/// correlation key reads defined in two places.
/// </summary>
public sealed class InboundEventFactoryTests
{
    [Fact]
    public void The_engine_host_builds_inbound_events_only_through_their_channel()
    {
        string host = Path.Combine(RepoPaths.Root(), "Hosts", "Wbskt.Workflow.Engine.Host");
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(host, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            if (File.ReadAllText(file).Contains("new InboundEvent(", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(host, file));
            }
        }

        Assert.True(offenders.Count == 0, $"Build these through the channel's Event factory instead: {string.Join(", ", offenders)}");
    }
}
