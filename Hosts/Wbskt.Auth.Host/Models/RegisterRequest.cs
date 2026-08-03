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
    string Password,

    /// <summary>
    /// An invitation token, for someone joining an existing tenant who has no account yet. Optional:
    /// without one the new account still gets its own tenant, so registration never depends on
    /// having been invited.
    /// <para>
    /// Supplying it here rather than requiring register-then-accept means the invitee follows one
    /// link and lands inside the tenant, instead of signing up and then having to find the
    /// invitation again.
    /// </para>
    /// </summary>
    [property: StringLength(255)]
    string? InvitationToken = null);
