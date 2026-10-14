using System.Net;

namespace Wbskt.Api.Client;

/// <summary>
/// A host answered with a failure status. <see cref="Problem"/> is its Problem Details body when it
/// sent one, so a caller can branch on <see cref="Code"/> rather than on the message.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, ApiProblem? problem, HttpMethod? method = null, Uri? requestUri = null)
        : base(Describe(statusCode, problem, method, requestUri))
    {
        StatusCode = statusCode;
        Problem = problem;
    }

    public HttpStatusCode StatusCode { get; }

    public ApiProblem? Problem { get; }

    /// <summary>The application's error code, or null when the body carried none.</summary>
    public string? Code => Problem?.Code;

    private static string Describe(HttpStatusCode statusCode, ApiProblem? problem, HttpMethod? method, Uri? requestUri)
    {
        string request = method is null ? "The request" : $"{method} {requestUri?.AbsolutePath}";
        string code = problem?.Code is { } c ? $" {c}" : string.Empty;
        string detail = problem?.Detail is { Length: > 0 } d ? $": {d}" : string.Empty;
        return $"{request} answered {(int)statusCode} {statusCode}{code}{detail}";
    }
}
