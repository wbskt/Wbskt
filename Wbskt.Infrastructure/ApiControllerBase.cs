using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Infrastructure.Security;
using Wbskt.Models;

namespace Wbskt.Infrastructure;

/// <summary>
/// Shared <see cref="Result"/> to <see cref="IActionResult"/> translation for every API controller,
/// through <see cref="ApiErrorResults"/>, the single place HTTP status codes are chosen for expected failures;
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

    /// <summary>
    /// An offset list's <see cref="Page{T}"/>, starting at <paramref name="offset"/>. The whole list's
    /// count also goes out as the <c>X-Total-Count</c> header for one release, for callers that read
    /// it from there.
    /// </summary>
    protected ActionResult<Page<T>> MapPage<T>(Result<IPagedList<T>> result, int offset)
    {
        if (result.IsFailure)
        {
            return MapError(result.Error);
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Ok(PageRequest.OffsetPage(result.Value, offset));
    }

    protected ActionResult MapError(Error error)
    {
        return ApiErrorResults.From(error, Logger);
    }
}
