using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Host.Models;

// Lengths mirror dbo.Users so that over-long input is rejected as a 400 here rather than surfacing
// as a truncation error from SQL. [ApiController] enforces these before the action runs.
public record RegisterRequest(
    [property: Required]
    [property: StringLength(50, MinimumLength = 3)]
    string Username,

    [property: Required]
    [property: EmailAddress]
    [property: StringLength(100)]
    string Email,

    [property: Required]
    [property: StringLength(128, MinimumLength = 12, ErrorMessage = "Password must be at least 12 characters.")]
    string Password);
