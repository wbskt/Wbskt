using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Infrastructure;

/// <summary>
/// Shared <see cref="Result"/> to <see cref="IActionResult"/> translation for every API controller.
/// This is the single place HTTP status codes are chosen for expected failures;
/// <c>GlobalExceptionMiddleware</c> only handles what escapes as an exception.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    private ILogger? _logger;

    /// <summary>
    /// Logger categorised by the concrete controller type, matching what an injected
    /// <c>ILogger&lt;TController&gt;</c> would produce. Resolved lazily so derived controllers
    /// do not have to thread one through their constructors. Falls back to a no-op logger when
    /// there is no request context — a directly constructed controller in a unit test — because
    /// mapping a result should never depend on ambient state being present.
    /// </summary>
    protected ILogger Logger => _logger ??= HttpContext?.RequestServices?
        .GetService<ILoggerFactory>()?
        .CreateLogger(GetType()) ?? NullLogger.Instance;

    /// <summary>
    /// The authenticated caller's internal user ID, taken from the identity that
    /// <c>IdentityMiddleware</c> established for this request. Absent context is reported as
    /// unauthenticated rather than throwing.
    /// </summary>
    protected Result<int> CurrentUserId()
    {
        var identityService = HttpContext?.RequestServices?.GetService<IIdentityService>();
        if (identityService is null || !identityService.TryGetUserIdentity(out var identity))
        {
            return Result<int>.Failure(Error.Unauthorized("AUTH_UNAUTHORIZED", "Unauthorized access."));
        }

        return Result<int>.Success(identity.UserId);
    }

    protected ActionResult MapResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return MapError(result.Error);
    }

    protected ActionResult<T> MapResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapError(result.Error);
    }

    protected ActionResult MapError(Error error)
    {
        // Failure is the "something broke" bucket, so it is the only arm that is a server fault
        // and the only one whose message cannot be trusted to be caller-safe.
        if (error.Type == ErrorType.Failure)
        {
            return ServerError(error);
        }

        Logger.LogWarning("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);

        return error.Type switch
        {
            ErrorType.Validation => BadRequest(error),
            ErrorType.NotFound => NotFound(error),
            ErrorType.Conflict => Conflict(error),

            // 401 means "we do not know who you are". Never use it for a permission denial, or a
            // client that redirects to login on 401 signs the user out over a missing permission.
            ErrorType.Unauthorized => Unauthorized(error),

            // Not Forbid(): that defers to the auth handler's challenge machinery. These are
            // bearer-token APIs, so a plain 403 carrying the error body is what callers expect.
            ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, error),
            _ => ServerError(error)
        };
    }

    /// <summary>
    /// The message on a <see cref="ErrorType.Failure"/> is typically raw exception text — SQL
    /// statements, column names, connection details. These hosts are publicly routed, so the
    /// detail is logged and a generic message is returned in its place. The code is preserved
    /// because it is a fixed application constant and is what support will ask for.
    /// </summary>
    private ObjectResult ServerError(Error error)
    {
        Logger.LogError("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);

        return StatusCode(
            StatusCodes.Status500InternalServerError,
            Error.Failure(error.Code, "An unexpected error occurred while processing the request."));
    }
}
