using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Contracts;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UsersController(ILogger<UsersController> logger, IUsersService usersService, IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public IActionResult UserLogin(UserLoginRequest request)
    {
        if (usersService.FindUserIdByEmailId(request.EmailId) <= 0)
        {
            logger.LogInformation("user {email} does not exist", request.EmailId);
            return Unauthorized($"user {request.EmailId} does not exist");
        }

        var valid = authService.ValidatePassword(request);
        if (!valid)
        {
            logger.LogInformation("credentials are incorrect");
            return Unauthorized("credentials are incorrect");
        }

        var user = usersService.GetUserByEmailId(request.EmailId);
        var token = authService.GenerateToken(user);
        return Ok(token);
    }

    [HttpPost("register")]
    public IActionResult UserRegistration(UserRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            request.UserName = request.EmailId;
        }

        // todo: verify email is unique
        var user = authService.RegisterUser(request);
        // string token = authService.GenerateToken(user);
        return Ok("User created. Please login");
    }
}
