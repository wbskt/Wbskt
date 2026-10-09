using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// <c>GET event-logs</c>, its summary and its CSV: how a query's filters become what the database is
/// asked for, and how the groups behind the audit log's views are made.
/// </summary>
public sealed class EventLogQueryTests
{
    private const int WorkspaceId = 7;
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IEventProvider> _provider = new();
    private EventLogFilter? _asked;
    private int _askedTake;

    private EventLogService CreateService()
    {
        _provider.Setup(p => p.GetLogsAsync(WorkspaceId, It.IsAny<EventLogFilter>(), It.IsAny<long?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, EventLogFilter, long?, int, CancellationToken>((_, filter, _, take, _) => (_asked, _askedTake) = (filter, take))
            .ReturnsAsync([]);
        return new EventLogService(_provider.Object, Mock.Of<IRegistrationPolicyService>(), Mock.Of<IClientQueryService>(), new FixedTime(Now), NullLogger<EventLogService>.Instance);
    }

    [Fact]
    public void Every_group_has_events_and_none_holds_device_traffic()
    {
        EventLogGroups.All.Keys.Should().BeEquivalentTo(["people", "clients", "policies", "workflows", "security"]);
        foreach (var (group, events) in EventLogGroups.All)
        {
            events.Should().NotBeEmpty(group);
            events.Should().NotIntersectWith(DeviceTrafficAttribute.EventNames, group);
        }

        EventLogGroups.All["clients"].Should().Contain(["ClientRenamedEvent", "ClientAutoApprovedEvent"]).And.NotContain("PolicyCreatedEvent");
        EventLogGroups.All["policies"].Should().Contain("PolicyCreatedEvent").And.NotContain("ClientAutoApprovedEvent");
        EventLogGroups.All["people"].Should().Contain("UserLoginFailedEvent");
        EventLogGroups.All["workflows"].Should().Contain("WorkflowPublishedEvent");
    }

    [Fact]
    public void Every_security_event_is_a_real_warning_or_error()
    {
        var events = typeof(DeviceTrafficAttribute).Assembly.GetTypes().ToDictionary(t => t.Name);

        foreach (var name in EventLogGroups.All["security"])
        {
            events.Should().ContainKey(name);
            var criticality = events[name].GetCustomAttributes(typeof(EventCriticalityAttribute), false).Cast<EventCriticalityAttribute>().Single().Criticality;
            criticality.Should().BeOneOf([EventCriticality.Warning, EventCriticality.Error], name);
        }
    }

    [Fact]
    public async Task Groups_and_names_together_keep_an_event_named_by_either()
    {
        var service = CreateService();

        await service.GetLogsAsync(WorkspaceId, new EventLogQuery { Group = ["policies"], EventNames = ["ClientDeletedEvent,ClientRenamedEvent", " WorkflowDeletedEvent "] }, null, 50);

        _asked!.EventNames.Should().BeEquivalentTo(EventLogGroups.All["policies"].Concat(["ClientDeletedEvent", "ClientRenamedEvent", "WorkflowDeletedEvent"]));
        _asked.ExcludeEventNames.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_group_is_a_bad_request()
    {
        var result = await CreateService().GetLogsAsync(WorkspaceId, new EventLogQuery { Group = ["people", "billing"] }, null, 50);

        result.Error.Code.Should().Be("EVENT_LOG_GROUP_UNKNOWN");
        result.Error.Type.Should().Be(ErrorType.Validation);
        _asked.Should().BeNull();
    }

    [Fact]
    public async Task Excluding_traffic_from_the_whole_log_drops_the_traffic_events()
    {
        await CreateService().GetLogsAsync(WorkspaceId, new EventLogQuery { Traffic = EventLogTraffic.Exclude }, null, 50);

        _asked!.EventNames.Should().BeNull();
        _asked.ExcludeEventNames.Should().BeEquivalentTo(DeviceTrafficAttribute.EventNames);
    }

    [Fact]
    public async Task Asking_only_for_traffic_while_excluding_it_finds_nothing_without_asking_the_database()
    {
        var trafficEvent = DeviceTrafficAttribute.EventNames[0];

        var result = await CreateService().GetLogsAsync(WorkspaceId, new EventLogQuery { EventNames = [trafficEvent], Traffic = EventLogTraffic.Exclude }, null, 50);

        result.Value.Items.Should().BeEmpty();
        result.Value.NextCursor.Should().BeNull();
        _asked.Should().BeNull();
    }

    [Fact]
    public async Task Workflow_user_range_and_search_reach_the_database_as_given()
    {
        var workflowRef = Guid.NewGuid();
        var userRef = Guid.NewGuid();

        await CreateService().GetLogsAsync(WorkspaceId, new EventLogQuery
        {
            WorkflowRefId = workflowRef,
            UserRefId = userRef,
            From = Now.AddDays(-3),
            To = Now.AddDays(-1),
            Q = "  porch 50%  "
        }, null, 50);

        _asked!.WorkflowRefId.Should().Be(workflowRef);
        _asked.UserRefId.Should().Be(userRef);
        _asked.FromUtc.Should().Be(Now.UtcDateTime.AddDays(-3));
        _asked.ToUtc.Should().Be(Now.UtcDateTime.AddDays(-1));
        _asked.Search.Should().Be("porch 50%");
    }

    [Fact]
    public async Task Without_a_range_the_list_reads_all_of_retention()
    {
        await CreateService().GetLogsAsync(WorkspaceId, new EventLogQuery(), null, 50);

        _asked!.FromUtc.Should().BeNull();
        _asked.ToUtc.Should().BeNull();
    }

    [Fact]
    public async Task A_backwards_range_or_a_long_search_is_a_bad_request()
    {
        var service = CreateService();

        (await service.GetLogsAsync(WorkspaceId, new EventLogQuery { From = Now, To = Now.AddDays(-1) }, null, 50)).Error.Code.Should().Be("TIME_RANGE_INVALID");
        (await service.GetLogsAsync(WorkspaceId, new EventLogQuery { Q = new string('x', EventLogService.MaxSearchLength + 1) }, null, 50)).Error.Code.Should().Be("EVENT_LOG_SEARCH_TOO_LONG");
        _asked.Should().BeNull();
    }

    [Fact]
    public void Search_text_matches_itself_literally()
    {
        EventProvider.EscapeLike(@"50%_off [x] a\b").Should().Be(@"50\%\_off \[x] a\\b");
    }

    [Fact]
    public async Task The_csv_reads_the_last_week_by_default_up_to_its_cap()
    {
        var csv = await CreateService().GetCsvAsync(WorkspaceId, new EventLogQuery());

        csv.Value.Should().StartWith(EventLogCsv.Header);
        _asked!.FromUtc.Should().Be(Now.UtcDateTime.AddDays(-7));
        _asked.ToUtc.Should().Be(Now.UtcDateTime);
        _askedTake.Should().Be(EventLogService.MaxExportRows);
    }

    [Fact]
    public void Csv_rows_carry_the_id_and_guard_their_text()
    {
        var entry = new EventLogResponse("ClientRenamedEvent", "{\"newName\":\"=porch, east\"}", EventCriticality.Info, null, Guid.Parse("22222222-2222-2222-2222-222222222222"), null,
            new DateTime(2026, 10, 9, 8, 30, 0, DateTimeKind.Utc), null, 42);

        var lines = EventLogCsv.Write([entry]).Split("\r\n");

        lines[1].Should().Be("42,2026-10-09T08:30:00.000Z,ClientRenamedEvent,Info,,22222222-2222-2222-2222-222222222222,,,\"{\"\"newName\"\":\"\"=porch, east\"\"}\"");
    }

    [Fact]
    public async Task The_summary_folds_events_into_groups_and_leaves_traffic_out_of_the_total_when_asked()
    {
        var traffic = DeviceTrafficAttribute.EventNames[0];
        _provider.Setup(p => p.CountAsync(WorkspaceId, Now.UtcDateTime.AddDays(-7), Now.UtcDateTime, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new EventLogCount("PolicyCreatedEvent", null, null, 2),
                new EventLogCount("ClientDeletedEvent", null, null, 3),
                new EventLogCount("UserLoginFailedEvent", null, null, 4),
                new EventLogCount(traffic, null, null, 100)
            ]);
        var service = CreateService();

        var withTraffic = await service.GetSummaryAsync(WorkspaceId, null, null, EventLogTraffic.Include);
        var withoutTraffic = await service.GetSummaryAsync(WorkspaceId, null, null, EventLogTraffic.Exclude);

        withTraffic.Value.Total.Should().Be(109);
        withoutTraffic.Value.Total.Should().Be(9);
        withTraffic.Value.Groups.Should().Contain(new Dictionary<string, long>
        {
            ["policies"] = 2,
            ["clients"] = 3,
            ["people"] = 4,
            ["workflows"] = 0,
            ["security"] = 7
        });
    }

    [Fact]
    public async Task The_summary_counts_each_event_person_and_source_without_traffic_when_asked()
    {
        var pika = Guid.NewGuid();
        var amal = Guid.NewGuid();
        var traffic = DeviceTrafficAttribute.EventNames[0];
        _provider.Setup(p => p.CountAsync(WorkspaceId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new EventLogCount("ClientRenamedEvent", pika, EventSource.Console, 5),
                new EventLogCount("ClientRenamedEvent", amal, EventSource.Api, 2),
                new EventLogCount("UserLoginFailedEvent", null, null, 3),
                new EventLogCount("PolicyRegistrationAttemptedOnDisabledEvent", null, EventSource.System, 1),
                new EventLogCount(traffic, pika, EventSource.Console, 40)
            ]);

        var summary = (await CreateService().GetSummaryAsync(WorkspaceId, null, null, EventLogTraffic.Exclude)).Value;

        summary.Events.Should().Equal(new Dictionary<string, long>
        {
            ["ClientRenamedEvent"] = 7,
            ["UserLoginFailedEvent"] = 3,
            ["PolicyRegistrationAttemptedOnDisabledEvent"] = 1
        });
        summary.People.Should().Equal(new Dictionary<Guid, long> { [pika] = 5, [amal] = 2 });
        summary.Sources.Should().Equal(new Dictionary<string, long> { ["Console"] = 5, ["Api"] = 2, ["System"] = 1 });
    }

    [Fact]
    public async Task Sources_severity_floor_and_a_live_cursor_reach_the_database()
    {
        var service = CreateService();

        await service.GetLogsAsync(WorkspaceId,
            new EventLogQuery { Source = ["console,system", " Workflow "], MinCriticality = EventCriticality.Warning, SinceId = 884213 }, null, 50);

        _asked!.Sources.Should().BeEquivalentTo([EventSource.Console, EventSource.System, EventSource.Workflow]);
        _asked.MinCriticality.Should().Be(EventCriticality.Warning);
        _asked.SinceId.Should().Be(884213);
    }

    [Theory]
    [InlineData("billing")]
    [InlineData("3")]
    public async Task An_unknown_source_is_a_bad_request(string source)
    {
        var result = await CreateService().GetLogsAsync(WorkspaceId, new EventLogQuery { Source = [source] }, null, 50);

        result.Error.Code.Should().Be("EVENT_LOG_SOURCE_UNKNOWN");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }
}

file sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
