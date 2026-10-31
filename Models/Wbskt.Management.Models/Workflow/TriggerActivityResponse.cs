namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// What a workflow trigger did with the latest messages that reached it, newest first, and where a
/// client trigger with a hold time stands. <see cref="Hold"/> is null for every other trigger.
/// </summary>
public sealed record TriggerActivityResponse(IReadOnlyList<TriggerOutcomeDto> Recent, TriggerHoldStateDto? Hold);

/// <param name="Outcome">
/// StartedRun, Queued, Dropped, Refused, Filtered, SecretMismatch or Idempotent from the dispatcher,
/// or Held when a hold trigger's filter matched.
/// </param>
/// <param name="RunRef">The run the message started, when it started one.</param>
public sealed record TriggerOutcomeDto(DateTimeOffset OccurredAt, string Channel, string? MessageType, string Outcome, string? CorrelationKey, Guid? RunRef);

/// <param name="State">holding, fired or clear.</param>
/// <param name="SinceAt">The first matching message of the current hold.</param>
/// <param name="DueAt">When the run starts if the filter keeps matching.</param>
public sealed record TriggerHoldStateDto(string State, DateTimeOffset? SinceAt, DateTimeOffset? DueAt, DateTimeOffset LastEventAt);
