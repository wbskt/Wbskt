using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>Editing a policy announces each field it changed, with the value before and after.</summary>
public sealed class PolicyChangeEventTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid PolicyRef = Guid.NewGuid();

    private readonly Mock<IRegistrationPolicyProvider> _provider = new();
    private readonly List<IEvent> _published = [];

    private RegistrationPolicyService CreateService(RegistrationPolicy before, RegistrationPolicy after)
    {
        _provider.Setup(p => p.FindByRefIdAsync(PolicyRef, It.IsAny<CancellationToken>())).ReturnsAsync(before);
        _provider.Setup(p => p.FindByIdAsync(before.Id, It.IsAny<CancellationToken>())).ReturnsAsync(after);
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<BaseEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IEvent, CancellationToken>((e, _) => { lock (_published) { _published.Add(e); } })
            .Returns(Task.CompletedTask);
        return new RegistrationPolicyService(_provider.Object, Mock.Of<IClientProvider>(), bus.Object, NullLogger<RegistrationPolicyService>.Instance);
    }

    private static RegistrationPolicy Policy(string name, int? maxClients, bool enabled) =>
        new() { Id = 5, RefId = PolicyRef, WorkspaceId = WorkspaceId, Name = name, MaxClients = maxClients, IsEnabled = enabled };

    [Fact]
    public async Task Each_changed_field_is_announced_with_its_old_and_new_value()
    {
        var service = CreateService(Policy("lab", 10, enabled: true), Policy("garden", 20, enabled: false));

        (await service.UpdateAsync(WorkspaceId, PolicyRef, new UpdateRegistrationPolicyRequest("garden", 20, false, false))).IsSuccess.Should().BeTrue();

        var renamed = _published.OfType<PolicyNameUpdatedEvent>().Should().ContainSingle().Subject;
        (renamed.OldName, renamed.NewName).Should().Be(("lab", "garden"));
        renamed.Changes.Should().Equal(new FieldChange("name", "lab", "garden"));
        var limit = _published.OfType<PolicyClientLimitUpdatedEvent>().Should().ContainSingle().Subject;
        (limit.OldLimit, limit.NewLimit).Should().Be((10, 20));
        limit.Changes.Should().Equal(new FieldChange("clientLimit", "10", "20"));
        _published.OfType<PolicyDisabledEvent>().Should().ContainSingle()
            .Which.Changes.Should().Equal(new FieldChange("enabled", "true", "false"));
    }

    [Fact]
    public async Task Turning_a_disabled_policy_back_on_is_announced()
    {
        var service = CreateService(Policy("lab", 10, enabled: false), Policy("lab", 10, enabled: true));

        await service.UpdateAsync(WorkspaceId, PolicyRef, new UpdateRegistrationPolicyRequest("lab", 10, false, true));

        _published.Should().ContainSingle().Which.Should().BeOfType<PolicyEnabledEvent>()
            .Which.Changes.Should().Equal(new FieldChange("enabled", "false", "true"));
    }

    // A member's open policy page follows another member's change only through these hub events, so
    // turning a policy on must reach it as turning it off does.
    [Fact]
    public void Every_policy_change_reaches_the_console_hub()
    {
        var policyEvents = typeof(PolicyEnabledEvent).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(PolicyEnabledEvent).Namespace && t.Name.StartsWith("Policy") && t.Name.EndsWith("Event"))
            .ToList();

        policyEvents.Should().Contain([typeof(PolicyEnabledEvent), typeof(PolicyDisabledEvent)]);
        policyEvents.Should().AllSatisfy(t =>
            t.GetCustomAttributes(typeof(SignalRNotifyAttribute), inherit: false).Should().ContainSingle(t.Name));
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_announces_nothing()
    {
        var service = CreateService(Policy("lab", 10, enabled: true), Policy("lab", 10, enabled: true));

        await service.UpdateAsync(WorkspaceId, PolicyRef, new UpdateRegistrationPolicyRequest("lab", 10, false, true));

        _published.Should().BeEmpty();
    }

    [Fact]
    public async Task An_edit_naming_no_field_is_refused()
    {
        var service = CreateService(Policy("lab", 10, enabled: true), Policy("lab", 10, enabled: true));

        var result = await service.UpdateAsync(WorkspaceId, PolicyRef, new UpdateRegistrationPolicyRequest());

        result.Error.Code.Should().Be("POLICY_UPDATE_EMPTY");
        _provider.Verify(p => p.UpdateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateRegistrationPolicyRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_limit_and_its_removal_together_are_refused()
    {
        var service = CreateService(Policy("lab", 10, enabled: true), Policy("lab", 10, enabled: true));

        var result = await service.UpdateAsync(WorkspaceId, PolicyRef, new UpdateRegistrationPolicyRequest(MaxClients: 5, RemoveMaxClients: true));

        result.Error.Code.Should().Be("POLICY_LIMIT_AMBIGUOUS");
    }

    [Fact]
    public async Task A_blank_name_is_refused_but_a_missing_one_is_kept()
    {
        var service = CreateService(Policy("lab", 10, enabled: true), Policy("lab", 10, enabled: false));

        (await service.UpdateAsync(WorkspaceId, PolicyRef, new UpdateRegistrationPolicyRequest(Name: " "))).Error.Code.Should().Be("POLICY_NAME_REQUIRED");
        (await service.UpdateAsync(WorkspaceId, PolicyRef, new UpdateRegistrationPolicyRequest(IsEnabled: false))).IsSuccess.Should().BeTrue();

        _provider.Verify(p => p.UpdateAsync(WorkspaceId, 5, new UpdateRegistrationPolicyRequest(null, null, null, false, false), It.IsAny<CancellationToken>()), Times.Once);
        _published.Should().ContainSingle().Which.Should().BeOfType<PolicyDisabledEvent>();
    }
}
