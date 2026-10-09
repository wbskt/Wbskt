namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Announces a run's write to a shared variable, so the audit log names the workflow and run that
/// wrote it. Mirrors <see cref="IToastPublisher"/>: the engine host supplies the implementation.
/// </summary>
public interface IVariableWritePublisher
{
    /// <summary>Never throws: a write that is already stored is not undone by a missed announcement.</summary>
    Task PublishAsync(VariableWrite write, CancellationToken ct);
}

/// <param name="ValueJson">The value after the write, as stored.</param>
public sealed record VariableWrite(Guid WorkflowRefId, int WorkflowId, Guid RunRefId, int WorkspaceId, string Name, string Operation, string ValueJson);
