using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>A run started on request: from the console, the API, or a parent's sub-workflow node. Keys: <c>manual:{workflowRef}</c>.</summary>
public sealed class ManualChannel : TriggerChannel
{
    public const string Name = "manual";

    public override string Kind => Name;

    public static string Key(string workflowRef) => $"{Name}:{workflowRef}";

    /// <param name="requestKey">The caller's idempotency key, so a retried request starts no second run; a fresh id when null.</param>
    public static InboundEvent Event(Guid workflowRef, JsonElement body, string? requestKey, DateTime receivedAt) => new(
        Name,
        [Key(workflowRef.ToString())],
        $"{Name}:{workflowRef}:{(string.IsNullOrWhiteSpace(requestKey) ? Guid.NewGuid().ToString() : requestKey)}",
        new Dictionary<string, JsonElement>
        {
            ["workflowDefinitionRefId"] = Json(workflowRef.ToString()),
            ["body"] = body
        },
        receivedAt);

    public override string CorrelationKey(InboundEvent evt) => Key(Required(evt, "workflowDefinitionRefId"));

    public override Task<TriggerRegistrationSpec?> RegisterAsync(BaseNode node, TriggerPublication publication, CancellationToken ct) =>
        Task.FromResult<TriggerRegistrationSpec?>(node is ManualTriggerNode
            ? new TriggerRegistrationSpec(Key(publication.Definition.RefId.ToString()), WorkflowConcurrencyPolicy.AllowParallel.ToString(), null)
            : null);
}
