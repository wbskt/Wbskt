namespace Wbskt.Management.Host.Controllers.Workflow;

/// <summary>
/// Shared limits for the anonymous public callback edge (WaitForHttp wakes and webhook triggers).
/// These endpoints are gated only by possession of a token/path, so they are throttled per client
/// IP and their request bodies are capped to bound how much attacker-controlled data can be
/// persisted into workflow state per call.
/// </summary>
internal static class PublicCallbackPolicy
{
    /// <summary>Named rate-limiting policy applied to the public callback controller.</summary>
    public const string RateLimitPolicy = "public-callbacks";

    /// <summary>Requests permitted per <see cref="RateLimitWindowSeconds"/> window, per client IP.</summary>
    public const int PermitsPerWindow = 60;

    /// <summary>Length of the fixed rate-limiting window, in seconds.</summary>
    public const int RateLimitWindowSeconds = 60;

    /// <summary>Maximum accepted request body size (128 KiB) for a callback payload.</summary>
    public const long MaxBodyBytes = 128 * 1024;
}
