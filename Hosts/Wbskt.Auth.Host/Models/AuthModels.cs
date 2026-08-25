using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Host.Models;

// Deliberately no [EmailAddress] here, unlike RegisterRequest. Login verifies a credential rather
// than accepting a new one: rejecting a malformed identifier with a 400 tells a caller that the
// value was the wrong *shape* instead of returning the same 401 every other failed login gets, and
// it would break the moment sign-in accepts a username as well as an email. Length and presence
// are still bounded — they cost nothing and keep junk out of the password hasher and the query.
public record LoginRequest(
    [Required]
    [StringLength(100)]
    string Email,

    [Required]
    [StringLength(128)]
    string Password);

public record LoginResponse(string AccessToken, string RefreshToken);

/// <summary>
/// Carries a refresh token for rotation or revocation. A record rather than a bare
/// <c>[FromBody] string</c> so the body is normal JSON and the endpoint documents itself.
/// Note this changed the wire format: the endpoint previously accepted a bare JSON string.
/// </summary>
public record RefreshTokenRequest(
    [Required]
    [StringLength(255)]
    string RefreshToken);

/// <summary>
/// Asks for a password-reset link. Deliberately no <c>[EmailAddress]</c>: the endpoint answers
/// identically for every address, and a 400 for a malformed one would be the one response that
/// differs.
/// </summary>
public record ForgotPasswordRequest(
    [Required]
    [StringLength(100)]
    string Email);

/// <summary>
/// Redeems a reset link. The password constraints mirror <see cref="RegisterRequest"/> — a reset is
/// the other way a password gets set, and the two must not disagree about what is acceptable.
/// </summary>
public record ResetPasswordRequest(
    [Required]
    [StringLength(255)]
    string Token,

    [Required]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Password must be at least 12 characters.")]
    string NewPassword);

public record VerifyEmailRequest(
    [Required]
    [StringLength(255)]
    string Token);

/// <summary>Asks for a fresh confirmation link. Same non-disclosure reasoning as
/// <see cref="ForgotPasswordRequest"/>.</summary>
public record ResendVerificationRequest(
    [Required]
    [StringLength(100)]
    string Email);
