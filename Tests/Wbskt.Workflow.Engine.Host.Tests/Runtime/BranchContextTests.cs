using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class BranchContextTests
{
    [Fact]
    public void BranchContext_with_local_state_replaces_local_state_only()
    {
        // Arrange
        var originalState = new Dictionary<string, JsonElement>
        {
            ["count"] = JsonDocument.Parse("1").RootElement.Clone()
        };
        var replacementState = new Dictionary<string, JsonElement>
        {
            ["count"] = JsonDocument.Parse("2").RootElement.Clone()
        };
        var triggerPayload = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonDocument.Parse("\"client-a\"").RootElement.Clone()
        };
        var startedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);
        var context = CreateContext(originalState, triggerPayload, startedAt);

        // Act
        var updated = context with { LocalState = replacementState };

        // Assert
        Assert.Same(replacementState, updated.LocalState);
        Assert.Same(triggerPayload, updated.TriggerPayload);
        Assert.Equal(context.RunId, updated.RunId);
        Assert.Equal(context.BranchId, updated.BranchId);
        Assert.Equal(context.WorkflowDefinitionId, updated.WorkflowDefinitionId);
        Assert.Equal(context.WorkflowDefinitionRefId, updated.WorkflowDefinitionRefId);
        Assert.Equal(context.Version, updated.Version);
        Assert.Equal(context.CurrentNodeId, updated.CurrentNodeId);
        Assert.Equal(context.Attempt, updated.Attempt);
        Assert.Equal(context.CorrelationKey, updated.CorrelationKey);
        Assert.Equal(context.StartedAt, updated.StartedAt);
        Assert.Same(originalState, context.LocalState);
    }

    [Theory]
    [InlineData("RunId")]
    [InlineData("BranchId")]
    [InlineData("WorkflowDefinitionId")]
    [InlineData("WorkflowDefinitionRefId")]
    [InlineData("Version")]
    [InlineData("CurrentNodeId")]
    [InlineData("Attempt")]
    [InlineData("CorrelationKey")]
    [InlineData("StartedAt")]
    public void BranchContext_with_local_state_preserves_other_fields(string fieldName)
    {
        // Arrange
        var originalState = new Dictionary<string, JsonElement>
        {
            ["count"] = JsonDocument.Parse("1").RootElement.Clone()
        };
        var replacementState = new Dictionary<string, JsonElement>
        {
            ["count"] = JsonDocument.Parse("2").RootElement.Clone()
        };
        var triggerPayload = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonDocument.Parse("\"client-a\"").RootElement.Clone()
        };
        var startedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);
        var context = CreateContext(originalState, triggerPayload, startedAt);

        // Act
        var updated = context with { LocalState = replacementState };

        // Assert
        switch (fieldName)
        {
            case "RunId":
                Assert.Equal(context.RunId, updated.RunId);
                break;
            case "BranchId":
                Assert.Equal(context.BranchId, updated.BranchId);
                break;
            case "WorkflowDefinitionId":
                Assert.Equal(context.WorkflowDefinitionId, updated.WorkflowDefinitionId);
                break;
            case "WorkflowDefinitionRefId":
                Assert.Equal(context.WorkflowDefinitionRefId, updated.WorkflowDefinitionRefId);
                break;
            case "Version":
                Assert.Equal(context.Version, updated.Version);
                break;
            case "CurrentNodeId":
                Assert.Equal(context.CurrentNodeId, updated.CurrentNodeId);
                break;
            case "Attempt":
                Assert.Equal(context.Attempt, updated.Attempt);
                break;
            case "CorrelationKey":
                Assert.Equal(context.CorrelationKey, updated.CorrelationKey);
                break;
            case "StartedAt":
                Assert.Equal(context.StartedAt, updated.StartedAt);
                break;
            default:
                throw new InvalidOperationException($"Unexpected field '{fieldName}'.");
        }
    }

    private static BranchContext CreateContext(
        IReadOnlyDictionary<string, JsonElement> localState,
        IReadOnlyDictionary<string, JsonElement> triggerPayload,
        DateTime startedAt)
    {
        return new BranchContext(
            10,
            20,
            30,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            4,
            "node-a",
            1,
            localState,
            triggerPayload,
            "corr-1",
            startedAt);
    }
}
