using System.Text.Json;
using Microsoft.Data.SqlClient;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>
/// A test run runs a frozen copy of the draft or the live version, so editing the draft while it runs
/// changes nothing under it, and the engine runs it as it runs any version. The copy registers no
/// triggers; its steps' side effects are real.
/// </summary>
public sealed class WorkflowTestRunService : IWorkflowTestRunService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string TriggerKindPrefix = "trigger:";
    private const int WorkflowOwnedElsewhereError = 50021; // THROW in dbo.WorkflowDefinition_InsertTestCopy
    private const int WorkflowDeletedError = 50022; // THROW in dbo.WorkflowDefinition_InsertTestCopy

    private readonly IWorkflowDefinitionProvider _definitions;
    private readonly IWorkflowDraftProvider _drafts;
    private readonly IWorkflowQueryService _queryService;
    private readonly IWorkflowEngineGateway _engine;
    private readonly IIdentityService _identityService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowTestRunService> _logger;

    public WorkflowTestRunService(
        IWorkflowDefinitionProvider definitions,
        IWorkflowDraftProvider drafts,
        IWorkflowQueryService queryService,
        IWorkflowEngineGateway engine,
        IIdentityService identityService,
        IEventBus eventBus,
        ILogger<WorkflowTestRunService> logger)
    {
        _definitions = definitions;
        _drafts = drafts;
        _queryService = queryService;
        _engine = engine;
        _identityService = identityService;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<StartTestRunResponse>> StartAsync(int workspaceId, Guid workflowRefId, StartTestRunRequest request, CancellationToken ct)
    {
        Result<TestRunSubject> subject = await LoadAsync(workspaceId, workflowRefId, request.Source, ct);
        if (subject.IsFailure)
        {
            return Result<StartTestRunResponse>.Failure(subject.Error);
        }

        WorkflowDefinition definition = subject.Value.Definition;
        WorkflowValidationResponse validation = await _queryService.ValidateAsync(workspaceId, definition, ct);
        if (!validation.IsValid)
        {
            string problems = string.Join("; ", validation.Issues.Where(i => i.Severity == "Error").Select(i => $"[{i.Code}] {i.Message}"));
            return Result<StartTestRunResponse>.Failure(Error.Validation("WORKFLOW_VALIDATION_FAILED", $"Fix the workflow's problems before a test run: {problems}"));
        }

        Result<Guid> trigger = ResolveTrigger(definition, request.TriggerNodeId);
        if (trigger.IsFailure)
        {
            return Result<StartTestRunResponse>.Failure(trigger.Error);
        }

        WorkflowDefinitionRow copy;
        try
        {
            copy = await _definitions.InsertTestCopyAsync(new WorkflowDefinitionRow
            {
                Id = 0,
                RefId = workflowRefId,
                Version = subject.Value.BaseVersion,
                WorkspaceId = workspaceId,
                Name = subject.Value.Name,
                Description = subject.Value.Description,
                IsEnabled = true,
                DefinitionJson = JsonSerializer.Serialize(definition with { IsEnabled = true }, SerializerOptions),
                PublishedBy = _identityService.GetUserIdentity().UserId,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }
        catch (SqlException ex) when (ex.Number is WorkflowOwnedElsewhereError or WorkflowDeletedError)
        {
            return Result<StartTestRunResponse>.Failure(WorkspaceOwnership.WorkflowNotFound);
        }

        EngineTestRunResponse response;
        try
        {
            response = await _engine.StartTestRunAsync(new EngineTestRunRequest(copy.Id, trigger.Value, request.Payload ?? []), ct);
        }
        catch
        {
            // The engine may or may not have started the run; the delete keeps the copy if it did.
            await _definitions.DeleteUnreferencedAsync(copy.Id, CancellationToken.None);
            throw;
        }

        if (response.Outcome != EngineTestRunOutcome.Started)
        {
            // Nothing started from it, so the copy would only wait for retention.
            await _definitions.DeleteUnreferencedAsync(copy.Id, ct);
        }

        switch (response.Outcome)
        {
            case EngineTestRunOutcome.Started when response.RunRefId is { } runRefId:
                _logger.LogInformation("Test run {RunRefId} of workflow '{WorkflowRefId}' started from its {Source}", runRefId, workflowRefId, request.Source);
                await _eventBus.PublishAsync(new WorkflowRunRequestedEvent(workflowRefId, workspaceId, runRefId, "TestStarted"), ct);
                return Result<StartTestRunResponse>.Success(new StartTestRunResponse(runRefId));

            case EngineTestRunOutcome.TestRunActive:
                return Result<StartTestRunResponse>.Failure(Error.Conflict(
                    "TEST_RUN_ACTIVE",
                    response.ActiveRunRefId is { } active
                        ? $"Test run {active} of this workflow is still going. Open it, or cancel it to start another."
                        : "A test run of this workflow is still going. Cancel it to start another."));

            case EngineTestRunOutcome.WorkspaceFull:
                return Result<StartTestRunResponse>.Failure(Error.Conflict(
                    "PLAN_RUN_LIMIT_REACHED",
                    "The plan is at its limit of runs going at once. Try again when one finishes."));

            case EngineTestRunOutcome.CreditsSpent:
                return Result<StartTestRunResponse>.Failure(Error.Conflict(
                    "PLAN_CREDIT_LIMIT_REACHED",
                    "The plan has used its workflow credits for this month. Runs start again on the 1st, or after an upgrade."));

            case EngineTestRunOutcome.WorkspaceCreditCapReached:
                return Result<StartTestRunResponse>.Failure(Error.Conflict(
                    "WORKSPACE_CREDIT_CAP_REACHED",
                    "This workspace has used its monthly credit cap. Runs start again on the 1st, or when the cap is raised."));

            default:
                throw new InvalidOperationException($"Unrecognised engine outcome '{response.Outcome}' for a test run of workflow '{workflowRefId}'.");
        }
    }

    private async Task<Result<TestRunSubject>> LoadAsync(int workspaceId, Guid workflowRefId, TestRunSource source, CancellationToken ct)
    {
        if (source == TestRunSource.Draft)
        {
            WorkflowDraftRow? draft = await _drafts.FindAsync(workflowRefId, workspaceId, ct);
            return draft is null
                ? Result<TestRunSubject>.Failure(WorkflowDraftErrors.NotFound)
                : Result<TestRunSubject>.Success(new TestRunSubject(Deserialize(draft.DefinitionJson, workflowRefId), draft.BaseVersion ?? 0, draft.Name, draft.Description));
        }

        var current = await WorkflowOwnershipCheck.LoadCurrentAsync(_definitions, _logger, workspaceId, workflowRefId, ct);
        return current.IsFailure
            ? Result<TestRunSubject>.Failure(current.Error)
            : Result<TestRunSubject>.Success(new TestRunSubject(Deserialize(current.Value.DefinitionJson, workflowRefId), current.Value.Version, current.Value.Name, current.Value.Description));
    }

    private static Result<Guid> ResolveTrigger(WorkflowDefinition definition, Guid? requested)
    {
        var triggers = definition.Nodes.Where(node => node.Kind.StartsWith(TriggerKindPrefix, StringComparison.Ordinal)).ToList();
        if (requested is { } nodeId)
        {
            return triggers.Any(node => node.NodeId == nodeId)
                ? Result<Guid>.Success(nodeId)
                : Result<Guid>.Failure(Error.Validation("TEST_RUN_TRIGGER_NOT_FOUND", $"The workflow has no trigger step {nodeId}."));
        }

        return triggers.Count == 1
            ? Result<Guid>.Success(triggers[0].NodeId)
            : Result<Guid>.Failure(Error.Validation("TEST_RUN_TRIGGER_REQUIRED", "The workflow has more than one trigger. Name the one to start from in triggerNodeId."));
    }

    private static WorkflowDefinition Deserialize(string json, Guid workflowRefId) =>
        JsonSerializer.Deserialize<WorkflowDefinition>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not deserialize workflow '{workflowRefId}' for a test run.");
}
