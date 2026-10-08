using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Wbskt.Infrastructure;

/// <summary>
/// The single place HTTP status codes are chosen for expected failures. <see cref="ApiControllerBase"/>
/// maps through it, and so do filters that refuse a request before a controller exists.
/// <c>GlobalExceptionMiddleware</c> only handles what escapes as an exception.
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
            ErrorType.Validation => new BadRequestObjectResult(error),
            ErrorType.NotFound => new NotFoundObjectResult(error),
            ErrorType.Conflict => new ConflictObjectResult(error),

            // 401 means "we do not know who you are". Never use it for a permission denial, or a
            // client that redirects to login on 401 signs the user out over a missing permission.
            ErrorType.Unauthorized => new UnauthorizedObjectResult(error),

            // Not Forbid(): that defers to the auth handler's challenge machinery. These are
            // bearer-token APIs, so a plain 403 carrying the error body is what callers expect.
            ErrorType.Forbidden => new ObjectResult(error) { StatusCode = StatusCodes.Status403Forbidden },
            _ => ServerError(error, logger)
        };
    }

    /// <summary>
    /// The message on a <see cref="ErrorType.Failure"/> is typically raw exception text — SQL
    /// statements, column names, connection details. These hosts are publicly routed, so the
    /// detail is logged and a generic message is returned in its place. The code is preserved
    /// because it is a fixed application constant and is what support will ask for.
    /// </summary>
    private static ObjectResult ServerError(Error error, ILogger logger)
    {
        logger.LogError("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);

        return new ObjectResult(Error.Failure(error.Code, "An unexpected error occurred while processing the request."))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }
}
