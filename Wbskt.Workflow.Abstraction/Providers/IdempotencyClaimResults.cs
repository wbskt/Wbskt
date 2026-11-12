namespace Wbskt.Workflow.Abstraction.Providers;

/// <summary>What an event claim's <c>ResultJson</c> can record before it is settled.</summary>
public static class IdempotencyClaimResults
{
    /// <summary>The event had waiting branches to wake (<c>IdempotencyKey_MarkResumed</c> writes it).</summary>
    public const string Resumed = """{"resumed":true}""";
}
