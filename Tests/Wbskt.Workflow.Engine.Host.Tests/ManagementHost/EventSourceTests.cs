using FluentAssertions;
using Wbskt.Events.Abstractions;
using Wbskt.Management.Host.Handlers.Events;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>Where each logged event came from, and what the stored event keeps of the caller.</summary>
public sealed class EventSourceTests
{
    [Theory]
    [InlineData("ClientRenamedEvent", null, true, EventSource.Api)]
    [InlineData("ClientRenamedEvent", null, false, EventSource.System)]
    [InlineData("ClientRenamedEvent", EventSource.Console, true, EventSource.Console)]
    [InlineData("ClientCommandEvent", EventSource.Workflow, false, EventSource.Workflow)]
    [InlineData("ClientConnectedEvent", null, false, EventSource.Device)]
    [InlineData("ClientMessageReceivedEvent", null, false, EventSource.Device)]
    [InlineData("ClientAutoApprovedEvent", null, false, EventSource.Device)]
    [InlineData("PolicyRegistrationLimitReachedEvent", null, false, EventSource.Device)]
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
        var stored = EventLoggerHandler.Without("""{"newName":"porch","ClientAddress":"81.2.69.160","userAgent":"Firefox","actorUserRefId":null}""", "clientAddress", "userAgent");

        stored.Should().Be("""{"newName":"porch","actorUserRefId":null}""");
    }
}
