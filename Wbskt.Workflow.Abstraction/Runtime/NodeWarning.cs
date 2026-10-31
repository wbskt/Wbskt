namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Something worth noting about a step that did not stop it, such as a placeholder with no value.
/// The branch loop writes it to the run's history as <paramref name="EventKind"/>, once per step.
/// </summary>
/// <param name="Payload">Serialized as the history event's payload. Never a value that may be secret.</param>
public sealed record NodeWarning(string EventKind, object Payload);
