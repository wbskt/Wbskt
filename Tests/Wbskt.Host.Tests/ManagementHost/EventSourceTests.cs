using System.Reflection;
using FluentAssertions;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Management.Host.Handlers.Events;
using Wbskt.Management.Host.Services;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>Where each logged event came from, and what the stored event keeps of the caller.</summary>
public sealed class EventSourceTests
{
    [Theory]
    [InlineData("ClientRenamedEvent", null, true, EventSource.Api)]
    [InlineData("ClientRenamedEvent", null, false, EventSource.System)]
    [InlineData("ClientRenamedEvent", EventSource.Console, true, EventSource.Console)]
    [InlineData("ClientCommandEvent", EventSource.Workflow, false, EventSource.Workflow)]
    [InlineData("ClientConnectedEvent", null, false, EventSource.Client)]
    [InlineData("ClientMessageReceivedEvent", null, false, EventSource.Client)]
    [InlineData("ClientAutoApprovedEvent", null, false, EventSource.Client)]
    [InlineData("PolicyRegistrationLimitReachedEvent", null, false, EventSource.Client)]
    [InlineData("WorkflowRunStartedEvent", null, false, EventSource.Workflow)]
    [InlineData("SystemErrorEvent", null, false, EventSource.System)]
    public void Every_event_has_a_source(string eventName, EventSource? stamped, bool hasUser, EventSource expected)
    {
        EventSources.Derive(eventName, stamped, hasUser).Should().Be(expected);
    }

    [Fact]
    public void Sign_ins_and_unknown_events_have_none_yet()
    {
        EventSources.Derive("UserLoginFailedEvent", null, true).Should().BeNull();
        EventSources.Derive("NotAnEvent", null, false).Should().BeNull();
    }

    [Fact]
    public void The_stored_event_drops_the_callers_address_and_user_agent()
    {
        var stored = EventDataPrivacy.Without("""{"newName":"porch","ClientAddress":"81.2.69.160","userAgent":"Firefox","actorUserRefId":null}""", "clientAddress", "userAgent");

        stored.Should().Be("""{"newName":"porch","actorUserRefId":null}""");
    }

    [Fact]
    public void The_stored_event_drops_every_internal_id_and_keeps_the_references()
    {
        var clientRef = Guid.NewGuid();
        var stored = EventDataPrivacy.Strip($$"""{"WorkspaceId":7,"clientId":12,"clientRefId":"{{clientRef}}","policyId":3,"actorUserId":5,"workspaceIds":[7,8],"ipAddress":"81.2.69.160","newName":"porch"}""");

        stored.Should().Be($$"""{"clientRefId":"{{clientRef}}","newName":"porch"}""");
    }

    [Fact]
    public void A_body_with_nothing_private_is_returned_as_it_is()
    {
        const string body = """{ "newName" : "porch" }""";

        EventDataPrivacy.Strip(body).Should().BeSameAs(body);
    }

    [Fact]
    public void Every_internal_id_an_event_carries_is_one_the_stored_event_drops()
    {
        static bool IsInternalId(PropertyInfo p) =>
            (p.Name.EndsWith("Id", StringComparison.Ordinal) && (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) is var type && (type == typeof(int) || type == typeof(long)))
            || (p.Name.EndsWith("Ids", StringComparison.Ordinal) && typeof(IEnumerable<int>).IsAssignableFrom(p.PropertyType));

        var missed = typeof(SignalRPrivateAttribute).Assembly.GetTypes()
            .Where(t => typeof(IEvent).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetProperties().Where(IsInternalId).Select(p => $"{t.Name}.{p.Name}"))
            .Where(n => !EventDataPrivacy.Names.Contains(n.Split('.')[1]))
            .ToList();

        missed.Should().BeEmpty("an internal id left out of the private names would reach the event log's readers");
    }
}
