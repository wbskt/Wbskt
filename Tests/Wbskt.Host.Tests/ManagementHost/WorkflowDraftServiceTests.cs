using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class WorkflowDraftServiceTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid RefId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly Mock<IWorkflowDraftProvider> _drafts = new();
    private readonly Mock<IWorkflowQueryService> _query = new();
    private readonly Mock<IWorkflowLifecycleService> _lifecycle = new();

    public WorkflowDraftServiceTests()
    {
        _query.Setup(x => x.ValidateAsync(WorkspaceId, It.IsAny<WorkflowDefinition?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowValidationResponse(true, []));
        _lifecycle.Setup(x => x.PublishAsync(WorkspaceId, WorkspaceRef, It.IsAny<WorkflowPublishRequest>(), It.IsAny<WorkflowPublishGuards>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkflowPublishResponse>.Success(new WorkflowPublishResponse(RefId, 5, "Published")));
    }

    [Fact]
    public async Task Publishing_a_current_draft_expects_its_base_version_and_deletes_its_revision()
    {
        Draft(revision: 3, baseVersion: 4, currentVersion: 4);

        var result = await Service().PublishAsync(WorkspaceId, WorkspaceRef, RefId, new WorkflowDraftPublishRequest(3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _lifecycle.Verify(x => x.PublishAsync(
            WorkspaceId,
            WorkspaceRef,
            It.Is<WorkflowPublishRequest>(r => r.RefId == RefId && r.Name == "Draft name"),
            new WorkflowPublishGuards(4, 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_new_workflows_draft_expects_no_version_yet()
    {
        Draft(revision: 1, baseVersion: null, currentVersion: null);

        await Service().PublishAsync(WorkspaceId, WorkspaceRef, RefId, new WorkflowDraftPublishRequest(1), CancellationToken.None);

        _lifecycle.Verify(x => x.PublishAsync(WorkspaceId, WorkspaceRef, It.IsAny<WorkflowPublishRequest>(), new WorkflowPublishGuards(0, 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_outdated_draft_is_refused_unless_forced()
    {
        Draft(revision: 2, baseVersion: 4, currentVersion: 5);

        var refused = await Service().PublishAsync(WorkspaceId, WorkspaceRef, RefId, new WorkflowDraftPublishRequest(2), CancellationToken.None);
        Assert.Equal("WORKFLOW_DRAFT_OUTDATED", refused.Error.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);

        var forced = await Service().PublishAsync(WorkspaceId, WorkspaceRef, RefId, new WorkflowDraftPublishRequest(2, Force: true), CancellationToken.None);
        Assert.True(forced.IsSuccess);
        _lifecycle.Verify(x => x.PublishAsync(WorkspaceId, WorkspaceRef, It.IsAny<WorkflowPublishRequest>(), new WorkflowPublishGuards(null, 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publishing_a_revision_other_than_the_drafts_is_a_conflict()
    {
        Draft(revision: 4, baseVersion: 1, currentVersion: 1);

        var result = await Service().PublishAsync(WorkspaceId, WorkspaceRef, RefId, new WorkflowDraftPublishRequest(3), CancellationToken.None);

        Assert.Equal("WORKFLOW_DRAFT_CONFLICT", result.Error.Code);
        _lifecycle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Publishing_without_a_draft_is_not_found()
    {
        var result = await Service().PublishAsync(WorkspaceId, WorkspaceRef, RefId, new WorkflowDraftPublishRequest(1), CancellationToken.None);

        Assert.Equal("WORKFLOW_DRAFT_NOT_FOUND", result.Error.Code);
    }

    [Fact]
    public async Task A_discard_that_removes_nothing_says_whether_the_draft_moved_on()
    {
        var missing = await Service().DiscardAsync(WorkspaceId, RefId, 1, CancellationToken.None);
        Assert.Equal("WORKFLOW_DRAFT_NOT_FOUND", missing.Error.Code);

        Draft(revision: 2, baseVersion: 1, currentVersion: 1);
        var moved = await Service().DiscardAsync(WorkspaceId, RefId, 1, CancellationToken.None);
        Assert.Equal("WORKFLOW_DRAFT_CONFLICT", moved.Error.Code);
    }

    [Fact]
    public async Task A_draft_reads_back_with_its_validation_and_whether_it_is_outdated()
    {
        Draft(revision: 2, baseVersion: 4, currentVersion: 5);
        var issues = new WorkflowValidationResponse(false, [new WorkflowValidationIssueDto("Error", "NO_TRIGGER", "No trigger.", null)]);
        _query.Setup(x => x.ValidateAsync(WorkspaceId, It.IsAny<WorkflowDefinition?>(), It.IsAny<CancellationToken>())).ReturnsAsync(issues);

        var result = await Service().GetAsync(WorkspaceId, RefId, CancellationToken.None);

        Assert.True(result.Value.Outdated);
        Assert.Equal(issues, result.Value.Validation);
        Assert.Equal(2, result.Value.Revision);
    }

    [Fact]
    public async Task A_draft_without_a_name_is_not_saved()
    {
        var result = await Service().SaveAsync(WorkspaceId, RefId, new WorkflowDraftSaveRequest(" ", null, null!), CancellationToken.None);

        Assert.Equal("WORKFLOW_DRAFT_INVALID", result.Error.Code);
        _drafts.VerifyNoOtherCalls();
    }

    private WorkflowDraftService Service()
    {
        var identity = new Mock<IIdentityService>();
        identity.Setup(x => x.GetUserIdentity()).Returns(new UserIdentity(9));
        return new WorkflowDraftService(_drafts.Object, _query.Object, _lifecycle.Object, identity.Object, Mock.Of<ILogger<WorkflowDraftService>>());
    }

    private void Draft(int revision, int? baseVersion, int? currentVersion)
    {
        _drafts.Setup(x => x.FindAsync(RefId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkflowDraftRow
        {
            RefId = RefId,
            WorkspaceId = WorkspaceId,
            BaseVersion = baseVersion,
            Revision = revision,
            Name = "Draft name",
            Description = null,
            DefinitionJson = $$"""{"workflowRefId":"{{RefId}}","version":0,"name":"Draft name","isEnabled":true,"nodes":[],"edges":[],"sharedVariableSchema":[],"createdAt":"2026-11-08T00:00:00Z"}""",
            CreatedBy = 9,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = 9,
            UpdatedByRefId = null,
            UpdatedAt = DateTime.UtcNow,
            CurrentVersion = currentVersion
        });
    }
}
