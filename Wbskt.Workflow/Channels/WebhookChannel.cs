using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>
/// A call to a workspace's webhook URL. Keys: <c>webhook:{workspaceRef}:{path}</c>. Workspace-scoped
/// so an author-chosen path is unique per workspace and the anonymous public callback for one
/// workspace can never fire another's trigger.
/// </summary>
public sealed class WebhookChannel : TriggerChannel
{
    public const string Name = "webhook";

    public override string Kind => Name;

    public static string Key(string workspaceRef, string path) => $"{Name}:{workspaceRef}:{path}";

    public static InboundEvent Event(Guid workspaceRef, string path, JsonElement body, string inboundEventId, string? secretHash) => new(
        Name,
        [Key(workspaceRef.ToString(), path)],
        inboundEventId,
        new Dictionary<string, JsonElement>
        {
            ["workspaceRefId"] = Json(workspaceRef.ToString()),
            ["webhookPath"] = Json(path),
            ["body"] = body
        },
        default)
    {
        // Carried beside the payload, never inside it: the payload is persisted as the run's trigger
        // data, and a secret written there would be readable from the run's history forever.
        SecretHash = secretHash
    };

    public override string CorrelationKey(InboundEvent evt) =>
        Key(Required(evt, "workspaceRefId"), Required(evt, "webhookPath"));

    public override Task<TriggerRegistrationSpec?> RegisterAsync(BaseNode node, TriggerPublication publication, CancellationToken ct) =>
        Task.FromResult<TriggerRegistrationSpec?>(node is WebhookTriggerNode trigger
            ? new TriggerRegistrationSpec(Key(publication.WorkspaceRef.ToString(), trigger.Config.Path), trigger.Config.ConcurrencyPolicy.ToString(), trigger.Config.CorrelationKey)
            {
                Filter = trigger.Config.Filter,
                WebhookSecret = trigger.Config.Secret
            }
            : null);
}
