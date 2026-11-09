using System.Text.RegularExpressions;
using Wbskt.Events;
using Wbskt.Events.Client;
using Wbskt.Host.Tests.Architecture;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// The message meter is counted by <c>dbo.ClientTraffic_InsertBatch</c> as the event logger writes client
/// traffic, so the plan's message allowance depends on both events it counts being logged as traffic.
/// </summary>
public sealed class MessageMeterTests
{
    [Fact]
    public void Both_events_the_meter_counts_are_logged_as_client_traffic()
    {
        Assert.Contains(nameof(ClientMessageReceivedEvent), ClientTrafficAttribute.EventNames);
        Assert.Contains(nameof(ClientCommandDeliveredEvent), ClientTrafficAttribute.EventNames);
    }

    [Fact]
    public void The_meter_counts_events_by_the_names_they_are_logged_under()
    {
        string sql = File.ReadAllText(Path.Combine(RepoPaths.Root(), "Databases", "Wbskt.Database", "StoredProcedures", "ClientTraffic_InsertBatch.sql"));
        List<string> counted = Regex.Matches(sql, @"N'(\w+Event)'").Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.Equal([nameof(ClientCommandDeliveredEvent), nameof(ClientMessageReceivedEvent)], counted.Order(StringComparer.Ordinal));
        Assert.All(counted, name => Assert.Contains(name, ClientTrafficAttribute.EventNames));
    }
}
