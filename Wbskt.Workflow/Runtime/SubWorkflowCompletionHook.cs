using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Runtime;

internal sealed class SubWorkflowCompletionHook(IInboundHub hub, IIdGenerator idGenerator) : ISubWorkflowCompletionHook
{
    public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct)
        => OnRunCompletedAsync(runRefId, terminalStatus, null, ct);

    public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, string? outputJson, CancellationToken ct)
    {
        // The output rides in the event beside the status, so the parent reads it as childResult.output.
        // A run with no End output (or one that ended some other way) delivers the status alone.
        JsonElement? output = outputJson is null ? null : JsonSerializer.Deserialize<JsonElement>(outputJson, EngineJson.Options);
        return hub.HandleAsync(ChildCompletedChannel.Event(runRefId, terminalStatus, idGenerator.NewId(), output), ct);
    }
}
