namespace Wbskt.Workflow.Engine.Contracts;

/// <summary>
/// The engine's backend-only inbound HTTP API, as both sides of it see it: the engine serves these
/// routes and the management host calls them. Neither host declares the routes, header or key rules
/// on its own.
/// </summary>
public static class EngineInboundApi
{
    /// <summary>The header every inbound call presents the shared key in.</summary>
    public const string ApiKeyHeader = "X-Wbskt-Api-Key";

    public const string ManualRoute = "api/inbound/manual";

    public const string SignalRoute = "api/inbound/signal";

    /// <summary>Starts a run of the workflow's manual trigger. A key names the request, so a retry of it starts no second run.</summary>
    public static string Manual(Guid workflowRefId, string? idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"{ManualRoute}/{workflowRefId}"
            : $"{ManualRoute}/{workflowRefId}?idempotencyKey={Uri.EscapeDataString(idempotencyKey)}";

    /// <summary>Signals a run. The name is the author's, so it is escaped as the path segment it becomes.</summary>
    public static string Signal(Guid runRefId, string signalName) =>
        $"{SignalRoute}/{runRefId}/{Uri.EscapeDataString(signalName)}";
}

/// <summary>A caller's name for one delivery, so its retries are recognised as the same one.</summary>
public static class IdempotencyKeys
{
    public const int MaxLength = 255;

    /// <summary>1 to <see cref="MaxLength"/> printable ASCII characters, with no spaces.</summary>
    public static bool IsValid(string key) =>
        key.Length is > 0 and <= MaxLength && key.All(c => c is >= '\x21' and <= '\x7e');
}
