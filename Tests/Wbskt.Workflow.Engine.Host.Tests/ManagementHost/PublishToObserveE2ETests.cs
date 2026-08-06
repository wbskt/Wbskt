using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class PublishToObserveE2ETests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Full_publish_deprecate_cycle_calls_correct_hooks()
    {
        var provider = new InMemoryWorkflowDefinitionProvider();
        var triggerService = new RecordingTriggerRegistrationService();
        var cache = new RecordingWorkflowDefinitionCache();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var service = new WorkflowDefinitionService(provider, triggerService, cache, new WorkflowValidator(), identity.Object, Mock.Of<ILogger<WorkflowDefinitionService>>());
        var request = CreatePublishRequest();

        var v1 = await service.PublishAsync(1, Guid.NewGuid(), request, CancellationToken.None);
        var v2 = await service.PublishAsync(1, Guid.NewGuid(), request, CancellationToken.None);
        var deprecateResult = await service.DeprecateAsync(1, request.RefId, CancellationToken.None);

        Assert.True(v1.IsSuccess);
        Assert.True(v2.IsSuccess);
        Assert.True(deprecateResult.IsSuccess);
        Assert.Equal(1, v1.Value.Version);
        Assert.Equal(2, v2.Value.Version);
        Assert.Equal([1, 2], triggerService.PublishedWorkflowDefinitionIds);
        Assert.Equal([1, 2], triggerService.DeprecatedWorkflowDefinitionIds);
        Assert.Equal([1, 2], cache.InvalidatedWorkflowDefinitionIds.Distinct().OrderBy(x => x));
    }

    [Fact]
    public async Task Publish_workflow_increments_version()
    {
        var provider = new InMemoryWorkflowDefinitionProvider();
        var identity = new Mock<IIdentityService>();
        identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
        var service = new WorkflowDefinitionService(provider, new RecordingTriggerRegistrationService(), new RecordingWorkflowDefinitionCache(), new WorkflowValidator(), identity.Object, Mock.Of<ILogger<WorkflowDefinitionService>>());
        var request = CreatePublishRequest();

        var first = await service.PublishAsync(1, Guid.NewGuid(), request, CancellationToken.None);
        var second = await service.PublishAsync(1, Guid.NewGuid(), request, CancellationToken.None);
        var current = await service.GetCurrentAsync(1, request.RefId, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(current.IsSuccess);
        Assert.Equal(1, first.Value.Version);
        Assert.Equal(2, second.Value.Version);
        Assert.Equal(2, current.Value.Version);
    }

    private static WorkflowPublishRequest CreatePublishRequest()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "Abstraction", "Fixtures", "greenhouse-workflow.json")));
        var refId = document.RootElement.GetProperty("workflowRefId").GetGuid();
        var def = document.Deserialize<Wbskt.Workflow.Abstraction.Models.WorkflowDefinition>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new WorkflowPublishRequest(refId, "Vent control + escalation", null, def!);
    }

    private sealed class InMemoryWorkflowDefinitionProvider : IWorkflowDefinitionProvider
    {
        private readonly Dictionary<Guid, List<WorkflowDefinitionRow>> _rows = new();

        public Task<Wbskt.Models.IPagedList<WorkflowDefinitionRow>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct) => Task.FromResult<Wbskt.Models.IPagedList<WorkflowDefinitionRow>>(new Wbskt.Models.PagedList<WorkflowDefinitionRow>(Array.Empty<WorkflowDefinitionRow>(), 0));
        private int _nextId;

        public Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct)
        {
            WorkflowDefinitionRow? row = _rows.GetValueOrDefault(refId)?.SingleOrDefault(candidate => candidate.Version == version);
            return Task.FromResult(row?.Id as int?);
        }

        public Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct)
        {
            WorkflowDefinitionRow row = _rows.Values.SelectMany(list => list).Single(candidate => candidate.Id == id);
            return Task.FromResult(row);
        }

        public Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct)
        {
            WorkflowDefinitionRow? row = _rows.GetValueOrDefault(refId)?.SingleOrDefault(candidate => candidate.Version == version);
            return row is null
                ? Task.FromException<WorkflowDefinitionRow>(new NotFoundException("missing"))
                : Task.FromResult(row);
        }

        public Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct)
        {
            WorkflowDefinitionRow? row = _rows.GetValueOrDefault(refId)?.SingleOrDefault(candidate => candidate.IsEnabled);
            return row is null
                ? Task.FromException<WorkflowDefinitionRow>(new NotFoundException("missing"))
                : Task.FromResult(row);
        }

        public Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct)
        {
            if (!_rows.TryGetValue(row.RefId, out List<WorkflowDefinitionRow>? versions))
            {
                versions = [];
                _rows.Add(row.RefId, versions);
            }

            // Mirrors WorkflowDefinition_Publish: the procedure assigns the version, the caller does
            // not supply one.
            int nextVersion = versions.Count == 0 ? 1 : versions.Max(candidate => candidate.Version) + 1;
            WorkflowDefinitionRow inserted = row with { Id = ++_nextId, Version = nextVersion };

            versions.Add(inserted);
            return Task.FromResult(inserted);
        }

        public Task DeprecateAsync(int id, CancellationToken ct) => SetEnabledAsync(id, false, ct);

        public Task SetEnabledAsync(int id, bool isEnabled, CancellationToken ct)
        {
            foreach (var pair in _rows)
            {
                int index = pair.Value.FindIndex(candidate => candidate.Id == id);
                if (index >= 0)
                {
                    pair.Value[index] = pair.Value[index] with { IsEnabled = isEnabled };
                    return Task.CompletedTask;
                }
            }

            throw new NotFoundException("missing");
        }

        public List<int> DeletedIds { get; } = [];

        public Task<bool> DeleteUnreferencedAsync(int id, CancellationToken ct)
        {
            DeletedIds.Add(id);
            foreach (var pair in _rows)
            {
                if (pair.Value.RemoveAll(candidate => candidate.Id == id) > 0)
                {
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }
    }

    private sealed class RecordingTriggerRegistrationService : ITriggerRegistrationService
    {
        public List<int> PublishedWorkflowDefinitionIds { get; } = [];
        public List<int> DeprecatedWorkflowDefinitionIds { get; } = [];

        public Task OnPublishedAsync(int workflowDefinitionId, Guid workspaceRef, CancellationToken ct)
        {
            PublishedWorkflowDefinitionIds.Add(workflowDefinitionId);
            return Task.CompletedTask;
        }

        public Task OnDeprecatedAsync(int workflowDefinitionId, CancellationToken ct)
        {
            DeprecatedWorkflowDefinitionIds.Add(workflowDefinitionId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingWorkflowDefinitionCache : IWorkflowDefinitionCache
    {
        public List<int> InvalidatedWorkflowDefinitionIds { get; } = [];

        public Task<Wbskt.Workflow.Abstraction.Models.WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public void Invalidate(int workflowDefinitionId)
        {
            InvalidatedWorkflowDefinitionIds.Add(workflowDefinitionId);
        }
    }
}
