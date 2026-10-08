using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowDefinitionServiceTests
{
    // Matches the WorkspaceId stamped on rows by CreateWorkflowRow / the fixture.
    private const int WorkspaceId = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Publish_first_version_returns_version_1()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var request = CreatePublishRequest();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("missing"));
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => row with { Id = 11, Version = 1 });
        var bus = new Mock<IEventBus>();
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), bus.Object, Mock.Of<ILogger<WorkflowDefinitionService>>());

        var response = await service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(1, response.Value.Version);
        Assert.Equal("Published", response.Value.Status);
        triggerService.Verify(x => x.OnPublishedAsync(11, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        triggerService.Verify(x => x.OnDeprecatedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowPublishedEvent>(e => e.WorkflowRefId == request.RefId && e.WorkflowId == 11 && e.WorkspaceId == WorkspaceId && e.Version == 1 && e.RestoredFromVersion == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_stamps_resolved_workspace_id_on_inserted_row()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var request = CreatePublishRequest();
        WorkflowDefinitionRow? inserted = null;
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("missing"));
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => { inserted = row; return row with { Id = 21 }; });
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        await service.PublishAsync(42, Guid.NewGuid(), request, CancellationToken.None);

        Assert.NotNull(inserted);
        Assert.Equal(42, inserted!.WorkspaceId);
    }

    [Fact]
    public async Task Reinstate_reenables_and_reregisters_triggers()
    {
        // Deprecating deregisters the triggers, so flipping the flag back is not enough - the
        // workflow would read as published and never fire.
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var refId = Guid.NewGuid();
        var workspaceRef = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(9, refId, 3, isEnabled: false));
        var bus = new Mock<IEventBus>();
        var service = CreateService(workflowProvider, triggerService, bus);

        var result = await service.ReinstateAsync(WorkspaceId, workspaceRef, refId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        triggerService.Verify(x => x.OnPublishedAsync(9, workspaceRef, It.IsAny<CancellationToken>()), Times.Once);
        workflowProvider.Verify(x => x.SetEnabledAsync(9, true, It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowReinstatedEvent>(e => e.WorkflowRefId == refId && e.WorkflowId == 9 && e.Version == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reinstate_rejects_a_workflow_that_is_already_enabled()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(9, refId, 3, isEnabled: true));
        var service = CreateService(workflowProvider, triggerService);

        var result = await service.ReinstateAsync(WorkspaceId, Guid.NewGuid(), refId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("WORKFLOW_ALREADY_ENABLED", result.Error.Code);
        triggerService.Verify(x => x.OnPublishedAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reinstate_undoes_its_registrations_when_enabling_fails()
    {
        // Registrations without an enabled definition would fire a workflow the operator believes is
        // switched off.
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(9, refId, 3, isEnabled: false));
        workflowProvider.Setup(x => x.SetEnabledAsync(9, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var service = CreateService(workflowProvider, triggerService);

        var result = await service.ReinstateAsync(WorkspaceId, Guid.NewGuid(), refId, CancellationToken.None);

        Assert.True(result.IsFailure);
        triggerService.Verify(x => x.OnDeprecatedAsync(9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rollback_republishes_the_old_definition_as_a_new_version()
    {
        // Append-only: the old row is untouched, so the runs of every version keep pointing at the
        // definition they actually ran.
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var refId = CreatePublishRequest().RefId;
        WorkflowDefinitionRow? inserted = null;

        workflowProvider.Setup(x => x.GetByRefIdVersionAsync(refId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(5, refId, 1, isEnabled: false));
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(6, refId, 2, isEnabled: true));
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => { inserted = row; return row with { Id = 30, Version = 3 }; });

        var bus = new Mock<IEventBus>();
        var service = new WorkflowDefinitionService(
            workflowProvider.Object, triggerService.Object,
            new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), bus.Object, Mock.Of<ILogger<WorkflowDefinitionService>>());

        var result = await service.RollbackAsync(WorkspaceId, Guid.NewGuid(), refId, 1, CancellationToken.None);
        bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowPublishedEvent>(e => e.WorkflowId == 30 && e.Version == 3 && e.RestoredFromVersion == 1),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Version);
        Assert.NotNull(inserted);
        // The superseded version is deprecated exactly as it would be by any other publish.
        workflowProvider.Verify(x => x.DeprecateAsync(6, It.IsAny<CancellationToken>()), Times.Once);
        // Version 1's own row is left alone.
        workflowProvider.Verify(x => x.DeprecateAsync(5, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rollback_returns_not_found_for_a_version_that_does_not_exist()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetByRefIdVersionAsync(refId, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("missing"));
        var service = CreateService(workflowProvider, new Mock<ITriggerRegistrationService>());

        var result = await service.RollbackAsync(WorkspaceId, Guid.NewGuid(), refId, 99, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("WORKFLOW_VERSION_NOT_FOUND", result.Error.Code);
    }

    private static WorkflowDefinitionService CreateService(
        Mock<IWorkflowDefinitionProvider> workflowProvider,
        Mock<ITriggerRegistrationService> triggerService,
        Mock<IEventBus>? bus = null)
    {
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));

        return new WorkflowDefinitionService(
            workflowProvider.Object, triggerService.Object,
            new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), (bus ?? new Mock<IEventBus>()).Object, Mock.Of<ILogger<WorkflowDefinitionService>>());
    }

    [Fact]
    public void Validate_reports_issues_without_publishing_anything()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var service = new WorkflowDefinitionService(
            workflowProvider.Object, Mock.Of<ITriggerRegistrationService>(),
            new WorkflowValidator(), Mock.Of<IIdentityService>(), Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        // A definition with no nodes at all: no triggers (warning) and nothing to run.
        var empty = CreatePublishRequest().Definition with { Nodes = [], Edges = [] };

        WorkflowValidationResponse response = service.Validate(empty);

        Assert.True(response.IsValid, "an empty definition has warnings but no errors");
        Assert.Contains(response.Issues, issue => issue.Code == "NO_TRIGGERS" && issue.Severity == "Warning");
        // Nothing was written.
        workflowProvider.VerifyNoOtherCalls();
    }

    [Fact]
    public void Validate_returns_errors_with_the_offending_node()
    {
        var service = new WorkflowDefinitionService(
            Mock.Of<IWorkflowDefinitionProvider>(), Mock.Of<ITriggerRegistrationService>(),
            new WorkflowValidator(), Mock.Of<IIdentityService>(), Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var definition = CreatePublishRequest().Definition;
        var duplicateEdge = definition.Edges.First();

        WorkflowValidationResponse response = service.Validate(definition with { Edges = [.. definition.Edges, duplicateEdge] });

        Assert.False(response.IsValid);
        var issue = Assert.Single(response.Issues, i => i.Code == "DUPLICATE_PORT_EDGE");
        Assert.Equal("Error", issue.Severity);
        Assert.NotNull(issue.NodeId);
    }

    [Fact]
    public async Task Publish_takes_the_version_from_the_inserted_row_not_a_precomputed_guess()
    {
        // The procedure assigns the version under HOLDLOCK. The service must report what came back,
        // not a number derived from its own earlier unlocked read - under concurrent publishes the
        // two can differ.
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var request = CreatePublishRequest();
        WorkflowDefinitionRow? submitted = null;

        // Stale read says v1 exists; the procedure actually lands on v9 because someone else
        // published in between.
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(5, request.RefId, 1, true));
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => { submitted = row; return row with { Id = 6, Version = 9 }; });

        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var response = await service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(9, response.Value.Version);
        // Nothing is pre-computed on the way in.
        Assert.NotNull(submitted);
        Assert.Equal(0, submitted!.Version);
    }

    [Fact]
    public async Task Publish_does_not_treat_a_database_error_as_a_missing_workflow()
    {
        // Security-relevant: swallowing every lookup exception would let a transient DB error look
        // like a brand-new workflow and skip the workspace-ownership check entirely.
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var request = CreatePublishRequest();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("connection reset"));

        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var response = await service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(response.IsFailure);
        workflowProvider.Verify(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_rolls_back_the_inserted_row_when_trigger_registration_fails()
    {
        // Otherwise the new version is current with no triggers registered - and because the previous
        // version has already been deprecated and deregistered, the workflow stops firing entirely.
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var request = CreatePublishRequest();
        var existing = CreateWorkflowRow(5, request.RefId, 1, true);
        var workspaceRef = Guid.NewGuid();

        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => row with { Id = 6, Version = 2 });
        workflowProvider.Setup(x => x.DeleteUnreferencedAsync(6, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        triggerService.Setup(x => x.OnPublishedAsync(6, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("registration exploded"));

        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var response = await service.PublishAsync(WorkspaceId, workspaceRef, request, CancellationToken.None);

        Assert.True(response.IsFailure);

        // The half-published row is removed...
        workflowProvider.Verify(x => x.DeleteUnreferencedAsync(6, It.IsAny<CancellationToken>()), Times.Once);
        // ...and the superseded version is put back, with the real workspace ref so its
        // workspace-scoped webhook keys match what callers will present.
        workflowProvider.Verify(x => x.SetEnabledAsync(5, true, It.IsAny<CancellationToken>()), Times.Once);
        triggerService.Verify(x => x.OnPublishedAsync(5, workspaceRef, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_second_version_increments_and_deprecates_first()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var request = CreatePublishRequest();
        var existing = CreateWorkflowRow(5, request.RefId, 1, true);
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => row with { Id = 6, Version = 2 });
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var response = await service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(2, response.Value.Version);
        workflowProvider.Verify(x => x.DeprecateAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        triggerService.Verify(x => x.OnDeprecatedAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        triggerService.Verify(x => x.OnPublishedAsync(6, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_new_version_over_other_workspace_is_a_conflict()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var request = CreatePublishRequest();
        var existing = CreateWorkflowRow(5, request.RefId, 1, true);
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var response = await service.PublishAsync(WorkspaceId + 99, Guid.NewGuid(), request, CancellationToken.None);
        
        Assert.True(response.IsFailure);
        Assert.Equal(ErrorType.Conflict, response.Error.Type);
        Assert.Equal("WORKFLOW_REF_TAKEN", response.Error.Code);
        workflowProvider.Verify(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrent_returns_dto()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(12, refId, 4, true));
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var result = await service.GetCurrentAsync(WorkspaceId, refId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(refId, result.Value.RefId);
        Assert.Equal(4, result.Value.Version);
        Assert.Equal("Published", result.Value.Status);
        Assert.Equal("Vent control + escalation", result.Value.Name);
    }

    [Fact]
    public async Task GetCurrent_in_other_workspace_is_not_found()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(12, refId, 4, true));
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var result = await service.GetCurrentAsync(WorkspaceId + 1, refId, CancellationToken.None);
        
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("WORKFLOW_NOT_FOUND", result.Error.Code);
    }

    [Fact]
    public async Task GetVersion_returns_dto()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetByRefIdVersionAsync(refId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(13, refId, 2, false));
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var result = await service.GetVersionAsync(WorkspaceId, refId, 2, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(refId, result.Value.RefId);
        Assert.Equal(2, result.Value.Version);
        Assert.Equal("Deprecated", result.Value.Status);
    }

    [Fact]
    public async Task Deprecate_marks_deprecated()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(14, refId, 6, true));
        var bus = new Mock<IEventBus>();
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), bus.Object, Mock.Of<ILogger<WorkflowDefinitionService>>());

        var result = await service.DeprecateAsync(WorkspaceId, refId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        workflowProvider.Verify(x => x.DeprecateAsync(14, It.IsAny<CancellationToken>()), Times.Once);
        triggerService.Verify(x => x.OnDeprecatedAsync(14, It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(x => x.PublishAsync(
            It.Is<WorkflowDeprecatedEvent>(e => e.WorkflowRefId == refId && e.WorkflowId == 14 && e.Version == 6),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_invokes_trigger_registration_hooks()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var request = CreatePublishRequest();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("missing"));
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => row with { Id = 17 });
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var response = await service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(response.IsSuccess);
        triggerService.Verify(x => x.OnPublishedAsync(17, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_invalid_definition_throws_ValidationException()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var request = new WorkflowPublishRequest(Guid.NewGuid(), "Invalid", null, null!);
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, new WorkflowValidator(), identity.Object, Mock.Of<IWorkflowEngineGateway>(), Mock.Of<IEventBus>(), Mock.Of<ILogger<WorkflowDefinitionService>>());

        var response = await service.PublishAsync(WorkspaceId, Guid.NewGuid(), request, CancellationToken.None);
        
        Assert.True(response.IsFailure);
        Assert.Equal(ErrorType.Validation, response.Error.Type);
        workflowProvider.Verify(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static WorkflowPublishRequest CreatePublishRequest()
    {
        var json = LoadFixture();
        using var document = JsonDocument.Parse(json);
        var refId = document.RootElement.GetProperty("workflowRefId").GetGuid();
        var def = document.Deserialize<Wbskt.Workflow.Abstraction.Models.WorkflowDefinition>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new WorkflowPublishRequest(refId, "Vent control + escalation", null, def!);
    }

    private static string LoadFixture()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return File.ReadAllText(Path.Combine(dir, "Abstraction", "Fixtures", "greenhouse-workflow.json"));
    }

    private static WorkflowDefinitionRow CreateWorkflowRow(int id, Guid refId, int version, bool isEnabled)
    {
        var json = LoadFixture();
        return new WorkflowDefinitionRow
        {
            Id = id,
            RefId = refId,
            Version = version,
            WorkspaceId = WorkspaceId,
            Name = "Vent control + escalation",
            Description = "Initial setup",
            IsEnabled = isEnabled,
            DefinitionJson = json,
            PublishedBy = 7,
            CreatedAt = DateTime.UtcNow
        };
    }
}
