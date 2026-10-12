using MassTransit;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>
/// The shared queue public callbacks reach the workflow engine through. The management host answers
/// a webhook or wake caller 202 once the broker holds the callback, and whichever engine instance is
/// leading takes it from there. The queue is durable, so a callback sent while the engine restarts or
/// fails over waits for it instead of being refused.
/// </summary>
/// <remarks>
/// These are commands, not events, so they never reach the event log. A webhook's body is whatever an
/// anonymous caller sent, and its secret travels only as a hash: a call that keeps failing is kept on
/// the queue's _error queue, where anyone who can read the broker could otherwise read a working secret.
/// </remarks>
public static class InboundCallbackQueue
{
    public const string Name = "workflow-inbound-callback";
}

/// <summary>
/// Run the workflows triggered by a call to a workspace's public webhook URL. <c>Body</c> is the
/// caller's JSON as sent. <c>SecretHash</c> is the hash (<c>WebhookSecretHash</c>) of whatever it
/// presented in <c>X-Wbskt-Secret</c>, compared by the engine against the trigger's;
/// <c>IdempotencyKey</c> is its <c>Idempotency-Key</c> header, already checked by the management host.
/// </summary>
// The wire name it had as InboundWebhook, so a call queued across the deploy is still handled.
[MessageUrn("Wbskt.Events.Workflow:InboundWebhook")]
public sealed record TriggerWebhook(Guid WorkspaceRef, string Path, string Body, string? SecretHash, string? IdempotencyKey) : ICommand
{
    public static string QueueName => InboundCallbackQueue.Name;
}

/// <summary>Wake the Wait For HTTP step waiting on this token. <c>Body</c> is the caller's JSON as sent.</summary>
[MessageUrn("Wbskt.Events.Workflow:InboundWake")]
public sealed record WakeWaitingStep(string Token, string Body) : ICommand
{
    public static string QueueName => InboundCallbackQueue.Name;
}
