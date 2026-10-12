using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Wbskt.Infrastructure;

/// <summary>
/// The single place HTTP status codes are chosen for expected failures. <see cref="ApiControllerBase"/>
/// maps through it, and so do filters that refuse a request before a controller exists.
/// <c>GlobalExceptionMiddleware</c> only handles what escapes as an exception. The body is
/// <see cref="Problems"/>' Problem Details, carrying the error's code.
/// </summary>
public static class ApiErrorResults
{
    public static ObjectResult From(Error error, ILogger logger)
    {
        // Failure is the "something broke" bucket, so it is the only arm that is a server fault
        // and the only one whose message cannot be trusted to be caller-safe.
        if (error.Type == ErrorType.Failure)
        {
            return ServerError(error, logger);
        }

        logger.LogWarning("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);

        return error.Type switch
        {
            ErrorType.Validation => new ProblemResult(error, StatusCodes.Status400BadRequest),
            ErrorType.NotFound => new ProblemResult(error, StatusCodes.Status404NotFound),
            ErrorType.Conflict => new ProblemResult(error, StatusCodes.Status409Conflict),

            // 401 means "we do not know who you are". Never use it for a permission denial, or a
            // client that redirects to login on 401 signs the user out over a missing permission.
            ErrorType.Unauthorized => new ProblemResult(error, StatusCodes.Status401Unauthorized),

            // Not Forbid(): that defers to the auth handler's challenge machinery. These are
            // bearer-token APIs, so a plain 403 carrying the error body is what callers expect.
            ErrorType.Forbidden => new ProblemResult(error, StatusCodes.Status403Forbidden),
            ErrorType.Unavailable => new RetryLaterResult(error),
            _ => ServerError(error, logger)
        };
    }

    /// <summary>
    /// An error as Problem Details. The trace id is added as the result is written, so it works the
    /// same from a controller and from a filter.
    /// </summary>
    public class ProblemResult : ObjectResult
    {
        public ProblemResult(Error error, int status) : base(Problems.From(error, status))
        {
            StatusCode = status;
            ContentTypes.Add(Problems.ContentType);
        }

        public ProblemDetails Problem => (ProblemDetails)Value!;

        public override void OnFormatting(ActionContext context)
        {
            Problem.Extensions[Problems.TraceIdKey] = Problems.TraceId(context.HttpContext);
            base.OnFormatting(context);
        }
    }

    /// <summary>How long a caller told 503 should wait before retrying.</summary>
    public const string RetryAfterSeconds = "5";

    /// <summary>
    /// A 503 carrying the problem and a <c>Retry-After</c> header. The header is set as the result is
    /// written, so it works the same from a controller and from a filter that has no response to hand.
    /// </summary>
    public sealed class RetryLaterResult : ProblemResult
    {
        public RetryLaterResult(Error error) : base(error, StatusCodes.Status503ServiceUnavailable)
        {
        }

        public override void OnFormatting(ActionContext context)
        {
            base.OnFormatting(context);
            context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds;
        }
    }

    /// <summary>
    /// The message on a <see cref="ErrorType.Failure"/> is typically raw exception text — SQL
    /// statements, column names, connection details. These hosts are publicly routed, so the
    /// detail is logged and a generic message is returned in its place. The code is preserved
    /// because it is a fixed application constant and is what support will ask for.
    /// </summary>
    private static ProblemResult ServerError(Error error, ILogger logger)
    {
        logger.LogError("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);

        return new ProblemResult(Error.Failure(error.Code, "An unexpected error occurred while processing the request."), StatusCodes.Status500InternalServerError);
    }
}
