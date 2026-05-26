using System.Reflection;
using System.Text.Json;
using Moq;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowDefinitionServiceTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Publish_first_version_returns_version_1()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var cache = new Mock<IWorkflowDefinitionCache>();
        var request = CreatePublishRequest();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("missing"));
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => row with { Id = 11 });
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, cache.Object, new WorkflowValidator());

        var response = await service.PublishAsync(request, CancellationToken.None);

        Assert.Equal(1, response.Version);
        Assert.Equal("Published", response.Status);
        triggerService.Verify(x => x.OnPublishedAsync(11, It.IsAny<CancellationToken>()), Times.Once);
        triggerService.Verify(x => x.OnDeprecatedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(x => x.Invalidate(11), Times.Once);
    }

    [Fact]
    public async Task Publish_second_version_increments_and_deprecates_first()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var cache = new Mock<IWorkflowDefinitionCache>();
        var request = CreatePublishRequest();
        var existing = CreateWorkflowRow(5, request.RefId, 1, true);
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => row with { Id = 6 });
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, cache.Object, new WorkflowValidator());

        var response = await service.PublishAsync(request, CancellationToken.None);

        Assert.Equal(2, response.Version);
        workflowProvider.Verify(x => x.DeprecateAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        triggerService.Verify(x => x.OnDeprecatedAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        triggerService.Verify(x => x.OnPublishedAsync(6, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.Invalidate(5), Times.Once);
        cache.Verify(x => x.Invalidate(6), Times.Once);
    }

    [Fact]
    public async Task Publish_invokes_trigger_registration_hooks()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var cache = new Mock<IWorkflowDefinitionCache>();
        var request = CreatePublishRequest();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("missing"));
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => row with { Id = 17 });
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, cache.Object, new WorkflowValidator());

        await service.PublishAsync(request, CancellationToken.None);

        triggerService.Verify(x => x.OnPublishedAsync(17, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.Invalidate(17), Times.Once);
    }

    [Fact]
    public async Task GetCurrent_returns_dto()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var cache = new Mock<IWorkflowDefinitionCache>();
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(12, refId, 4, true));
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, cache.Object, new WorkflowValidator());

        var dto = await service.GetCurrentAsync(refId, CancellationToken.None);

        Assert.Equal(refId, dto.RefId);
        Assert.Equal(4, dto.Version);
        Assert.Equal("Published", dto.Status);
        Assert.Equal("Vent control + escalation", dto.Name);
    }

    [Fact]
    public async Task GetVersion_returns_dto()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var cache = new Mock<IWorkflowDefinitionCache>();
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetByRefIdVersionAsync(refId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(13, refId, 2, false));
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, cache.Object, new WorkflowValidator());

        var dto = await service.GetVersionAsync(refId, 2, CancellationToken.None);

        Assert.Equal(refId, dto.RefId);
        Assert.Equal(2, dto.Version);
        Assert.Equal("Deprecated", dto.Status);
    }

    [Fact]
    public async Task Deprecate_marks_deprecated()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var cache = new Mock<IWorkflowDefinitionCache>();
        var refId = Guid.NewGuid();
        workflowProvider.Setup(x => x.GetCurrentByRefIdAsync(refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWorkflowRow(14, refId, 6, true));
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, cache.Object, new WorkflowValidator());

        await service.DeprecateAsync(refId, CancellationToken.None);

        workflowProvider.Verify(x => x.DeprecateAsync(14, It.IsAny<CancellationToken>()), Times.Once);
        triggerService.Verify(x => x.OnDeprecatedAsync(14, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.Invalidate(14), Times.Once);
    }

    [Fact]
    public async Task Publish_unknown_refId_returns_not_found_when_getting_current_after_deprecate()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var cache = new Mock<IWorkflowDefinitionCache>();
        var request = CreatePublishRequest();
        workflowProvider.SetupSequence(x => x.GetCurrentByRefIdAsync(request.RefId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("missing"))
            .ThrowsAsync(new NotFoundException("missing"));
        workflowProvider.Setup(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinitionRow row, CancellationToken _) => row with { Id = 15 });
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, cache.Object, new WorkflowValidator());

        await service.PublishAsync(request, CancellationToken.None);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetCurrentAsync(request.RefId, CancellationToken.None));
    }

    [Fact]
    public async Task Publish_invalid_definition_throws_ValidationException()
    {
        var workflowProvider = new Mock<IWorkflowDefinitionProvider>();
        var triggerService = new Mock<ITriggerRegistrationService>();
        var cache = new Mock<IWorkflowDefinitionCache>();
        var request = new WorkflowPublishRequest(Guid.NewGuid(), "Invalid", null, JsonSerializer.SerializeToElement(new
        {
            workflowRefId = Guid.Empty,
            version = 0,
            workspaceId = 1,
            name = "Invalid",
            description = (string?)null,
            isEnabled = true,
            nodes = Array.Empty<object>(),
            edges = Array.Empty<object>(),
            sharedVariableSchema = Array.Empty<object>(),
            createdAt = DateTime.UtcNow,
            publishedBy = 1
        }, SerializerOptions));
        var service = new WorkflowDefinitionService(workflowProvider.Object, triggerService.Object, cache.Object, new WorkflowValidator());

        await Assert.ThrowsAsync<ValidationException>(() => service.PublishAsync(request, CancellationToken.None));

        workflowProvider.Verify(x => x.InsertAsync(It.IsAny<WorkflowDefinitionRow>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static WorkflowPublishRequest CreatePublishRequest()
    {
        var json = LoadFixture();
        using var document = JsonDocument.Parse(json);
        var refId = document.RootElement.GetProperty("workflowRefId").GetGuid();
        return new WorkflowPublishRequest(refId, "Vent control + escalation", null, document.RootElement.Clone());
    }

    private static string LoadFixture()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return File.ReadAllText(Path.Combine(dir, "Abstraction", "Fixtures", "greenhouse-workflow.json"));
    }

    private static WorkflowDefinitionRow CreateWorkflowRow(int id, Guid refId, int version, bool isEnabled)
    {
        return new WorkflowDefinitionRow
        {
            Id = id,
            RefId = refId,
            Version = version,
            WorkspaceId = 1,
            Name = "Vent control + escalation",
            Description = null,
            IsEnabled = isEnabled,
            DefinitionJson = LoadFixture(),
            PublishedBy = 1,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }
}
