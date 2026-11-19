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

    /// <summary>
    /// The authenticated caller's internal id and the public reference their token names, for a
    /// service that needs both and should not read the user back to recover the reference.
    /// </summary>
    protected Result<(int UserId, Guid UserRef)> CurrentUser()
    {
        var identityService = HttpContext?.RequestServices?.GetService<IIdentityService>();
        if (identityService is null || !identityService.TryGetUserIdentity(out var identity) || identity.UserRefId is not { } userRef)
        {
            return Result<(int, Guid)>.Failure(Error.Unauthorized("AUTH_UNAUTHORIZED", "Unauthorized access."));
        }

        return Result<(int, Guid)>.Success((identity.UserId, userRef));
    }

    /// <summary>
    /// The caller's address as the forwarded headers resolved it (the real client, not the proxy), or
    /// <c>unknown</c>. For logs and the audit trail, never for a security decision.
    /// </summary>
    protected string CallerIpAddress() => HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

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

    /// <summary>An offset list's <see cref="Page{T}"/>, starting at <paramref name="offset"/>.</summary>
    protected ActionResult<Page<T>> MapPage<T>(Result<IPagedList<T>> result, int offset)
    {
        if (result.IsFailure)
        {
            return MapError(result.Error);
        }

        return Ok(PageRequest.OffsetPage(result.Value, offset));
    }

    protected ActionResult MapError(Error error)
    {
        return ApiErrorResults.From(error, Logger);
    }
}
