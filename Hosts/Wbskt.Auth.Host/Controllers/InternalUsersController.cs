using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// The caller's internal user id, which the management host keys its records by. A token names its
/// user by RefId only (#306), so a host that needs the id asks here, once per user. Internal, under
/// <see cref="InternalRoutes.Prefix"/>, which no public router serves.
/// </summary>
[Route(InternalRoutes.Prefix + "/users")]
[ApiController]
[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
public class InternalUsersController : ApiControllerBase
{
    [HttpGet("me")]
    public ActionResult<CurrentUserResponse> GetCurrentUser()
    {
        var userId = CurrentUserId();
        return userId.IsFailure ? MapError(userId.Error) : new CurrentUserResponse(userId.Value);
    }
}
