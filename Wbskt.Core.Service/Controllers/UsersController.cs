using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UsersController(
    ILogger<UsersController> logger,
    IUsersReader usersReader,
    IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> UserLogin(UserLoginRequest request, CancellationToken cancellationToken)
    {
        var userId = await usersReader.FindByEmailIdAsync(request.EmailId, cancellationToken);
        if (userId <= 0)
        {
            logger.LogInformation("user {email} does not exist", request.EmailId);
            return Unauthorized($"user {request.EmailId} does not exist");
        }

        var valid = await authService.ValidatePassword(request, cancellationToken);
        if (!valid)
        {
            logger.LogInformation("credentials are incorrect");
            return Unauthorized("credentials are incorrect");
        }

        var user = await usersReader.GetByEmailIdAsync(request.EmailId, cancellationToken);
        var token = authService.GenerateToken(user!);
        return Ok(token);
    }

    [HttpPost("register")]
    public async Task<IActionResult> UserRegistration(UserRegistrationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            request.UserName = request.EmailId;
        }

        var userId = await usersReader.FindByEmailIdAsync(request.EmailId, cancellationToken);
        if (userId > 0)
        {
            logger.LogInformation("user {email} already exists", request.EmailId);
            return Conflict($"user {request.EmailId} already exists");
        }
        
        var user = await authService.RegisterUser(request, cancellationToken);
        return Ok("User created. Please login");
    }
}
