using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Models;

/// <summary>
/// Fields of the signed-in account to change; one left out stays as it is. The username follows the
/// same rules as at sign-up. The address changes through <c>change-email</c>, since it must be proved.
/// </summary>
public record UpdateAccountRequest(
    [StringLength(50, MinimumLength = 3)]
    string? Username = null);

/// <summary>A new address for the account, confirmed with the password.</summary>
public record ChangeEmailRequest(
    [Required]
    [EmailAddress]
    [StringLength(100)]
    string NewEmail,

    [Required]
    [StringLength(128)]
    string Password);

/// <summary>The token from a confirm-email-change or revert-email-change link.</summary>
public record EmailChangeTokenRequest(
    [Required]
    [StringLength(255)]
    string Token);
