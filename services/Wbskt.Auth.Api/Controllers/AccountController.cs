using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Api.Models;

namespace Wbskt.Auth.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AccountController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = new ApplicationUser { UserName = model.Email, Email = model.Email };
        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            // Assign default role
            await _userManager.AddToRoleAsync(user, "User");
            return Ok(new { Message = "User registered successfully" });
        }

        return BadRequest(result.Errors);
    }
}

public class RegisterRequest
{
    public required string Email { get; set; }
    public required string Password { get; set; }
}
