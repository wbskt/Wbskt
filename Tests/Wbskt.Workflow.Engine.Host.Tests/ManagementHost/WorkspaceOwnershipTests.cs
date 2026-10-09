using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// Inside a workspace, a reference to another workspace's resource gets the same 404 as one that
/// names nothing, so a caller cannot learn that a guessed reference is real. See "The ID Boundary"
/// in Docs/Coding.Conventions.md.
/// </summary>
public sealed class WorkspaceOwnershipTests
{
    private const int WorkspaceId = 7;
    private const int OtherWorkspaceId = 99;

    [Fact]
    public async Task A_resource_the_workspace_owns_is_returned()
    {
        var policy = Policy(WorkspaceId);

        var result = await WorkspaceOwnership.LoadAsync(WorkspaceId, () => Task.FromResult<RegistrationPolicy?>(policy), WorkspaceOwnership.PolicyNotFound);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(policy);
    }

    [Fact]
    public async Task Another_workspaces_resource_reads_exactly_like_a_missing_one()
    {
        var foreign = await WorkspaceOwnership.LoadAsync(WorkspaceId, () => Task.FromResult<RegistrationPolicy?>(Policy(OtherWorkspaceId)), WorkspaceOwnership.PolicyNotFound);
        var missing = await WorkspaceOwnership.LoadAsync(WorkspaceId, () => Task.FromResult<RegistrationPolicy?>(null), WorkspaceOwnership.PolicyNotFound);

        foreign.Error.Should().Be(WorkspaceOwnership.PolicyNotFound);
        missing.Error.Should().Be(foreign.Error);
        foreign.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task A_lookup_that_fails_for_another_reason_is_not_reported_as_not_found()
    {
        var load = () => WorkspaceOwnership.LoadAsync<RegistrationPolicy>(WorkspaceId, () => throw new TimeoutException("db"), WorkspaceOwnership.PolicyNotFound);

        await load.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Updating_another_workspaces_policy_is_not_found_and_changes_nothing()
    {
        var foreign = Policy(OtherWorkspaceId);
        var provider = new Mock<IRegistrationPolicyProvider>();
        provider.Setup(p => p.FindByRefIdAsync(foreign.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var bus = new Mock<IEventBus>();
        var service = new RegistrationPolicyService(provider.Object, Mock.Of<IClientProvider>(), bus.Object, NullLogger<RegistrationPolicyService>.Instance);

        var update = await service.UpdateAsync(WorkspaceId, foreign.RefId, new UpdateRegistrationPolicyRequest("lab", null, false, true));
        var rotate = await service.RotatePinAsync(WorkspaceId, foreign.RefId);
        var disable = await service.DisableAsync(WorkspaceId, foreign.RefId);

        update.Error.Should().Be(WorkspaceOwnership.PolicyNotFound);
        rotate.Error.Should().Be(WorkspaceOwnership.PolicyNotFound);
        disable.Error.Should().Be(WorkspaceOwnership.PolicyNotFound);
        provider.Verify(p => p.UpdateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateRegistrationPolicyRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        provider.Verify(p => p.RotatePinAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        provider.Verify(p => p.DisableAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Templates_filtered_by_another_workspaces_policy_are_not_found_rather_than_an_empty_page()
    {
        var foreignPolicy = Policy(OtherWorkspaceId);
        var policies = new Mock<IRegistrationPolicyProvider>();
        policies.Setup(p => p.FindByRefIdAsync(foreignPolicy.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(foreignPolicy);
        var templates = new Mock<IMessageTemplateProvider>();
        var service = new MessageTemplateService(templates.Object, policies.Object, NullLogger<MessageTemplateService>.Instance);

        var result = await service.GetAllAsync(WorkspaceId, foreignPolicy.RefId, 0, 50);

        result.Error.Should().Be(WorkspaceOwnership.PolicyNotFound);
        templates.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Another_workspaces_template_is_not_found_and_not_deleted()
    {
        var template = new MessageTemplate { Id = 3, RefId = Guid.NewGuid(), WorkspaceId = OtherWorkspaceId, Name = "reboot", MessageType = "reboot", PayloadJson = "{}" };
        var templates = new Mock<IMessageTemplateProvider>();
        templates.Setup(p => p.FindByRefIdAsync(template.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        var service = new MessageTemplateService(templates.Object, Mock.Of<IRegistrationPolicyProvider>(), NullLogger<MessageTemplateService>.Instance);

        var result = await service.DeleteAsync(WorkspaceId, template.RefId);

        result.Error.Should().Be(WorkspaceOwnership.TemplateNotFound);
        templates.Verify(p => p.DeleteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Event_logs_filtered_by_another_workspaces_policy_or_client_are_not_found()
    {
        var policyRef = Guid.NewGuid();
        var clientRef = Guid.NewGuid();
        var policies = new Mock<IRegistrationPolicyService>();
        policies.Setup(s => s.FindInWorkspaceAsync(WorkspaceId, policyRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RegistrationPolicy>.Failure(WorkspaceOwnership.PolicyNotFound));
        var clients = new Mock<IClientService>();
        clients.Setup(s => s.EnsureClientInWorkspaceAsync(WorkspaceId, clientRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure(WorkspaceOwnership.ClientNotFound));
        var logs = new Mock<IEventLogService>();
        var controller = new EventLogsController(logs.Object, policies.Object, clients.Object);

        var byPolicy = await controller.GetLogs(WorkspaceId, null, null, policyRef, null);
        var byClient = await controller.GetLogs(WorkspaceId, null, null, null, clientRef);

        Assert.IsType<NotFoundObjectResult>(byPolicy.Result);
        Assert.IsType<NotFoundObjectResult>(byClient.Result);
        logs.VerifyNoOtherCalls();
    }

    private static RegistrationPolicy Policy(int workspaceId)
    {
        return new RegistrationPolicy { Id = 5, RefId = Guid.NewGuid(), WorkspaceId = workspaceId, Name = "lab", IsEnabled = true };
    }
}
