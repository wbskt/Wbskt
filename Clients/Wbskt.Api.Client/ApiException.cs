using System.Net;

namespace Wbskt.Api.Client;

/// <summary>
/// A host answered with a failure status. <see cref="Problem"/> is its Problem Details body when it
/// sent one, so a caller can branch on <see cref="Code"/> rather than on the message, and
/// <see cref="RetryAfter"/> says when a 429 or 503 may be tried again.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, ApiProblem? problem, HttpMethod? method = null, Uri? requestUri = null, TimeSpan? retryAfter = null)
        : base(Describe(statusCode, problem, method, requestUri))
    {
        StatusCode = statusCode;
        Problem = problem;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public ApiProblem? Problem { get; }

    /// <summary>The application's error code, or null when the body carried none.</summary>
    public string? Code => Problem?.Code;

    /// <summary>How long the host asked the caller to wait (its <c>Retry-After</c>), or null when it named no time.</summary>
    public TimeSpan? RetryAfter { get; }

    private static string Describe(HttpStatusCode statusCode, ApiProblem? problem, HttpMethod? method, Uri? requestUri)
    {
        string request = method is null ? "The request" : $"{method} {requestUri?.AbsolutePath}";
        string code = problem?.Code is { } c ? $" {c}" : string.Empty;
        string detail = problem?.Detail is { Length: > 0 } d ? $": {d}" : string.Empty;
        return $"{request} answered {(int)statusCode} {statusCode}{code}{detail}";
    }
}
