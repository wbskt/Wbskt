using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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

/// <summary>
/// A token pair. <see cref="RefreshToken"/> is left out when it went into the HttpOnly refresh cookie
/// instead (the caller sent <c>X-Refresh-Token-Transport: cookie</c>).
/// </summary>
public record LoginResponse(
    string AccessToken,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RefreshToken);

/// <summary>
/// Carries a refresh token for rotation or revocation. A record rather than a bare
/// <c>[FromBody] string</c> so the body is normal JSON and the endpoint documents itself.
/// Note this changed the wire format: the endpoint previously accepted a bare JSON string.
/// The token may be left out, or the body omitted, by a caller using the refresh cookie.
/// </summary>
public record RefreshTokenRequest(
    [StringLength(255)]
    string? RefreshToken);

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

/// <summary>
/// Changes the signed-in user's password. The current password is required even though the caller
/// holds a token: a session left open on a shared machine must not be enough to take the account.
/// The new password follows the same rules as <see cref="RegisterRequest"/>.
/// </summary>
public record ChangePasswordRequest(
    [Required]
    [StringLength(128)]
    string CurrentPassword,

    [Required]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Password must be at least 12 characters.")]
    string NewPassword);

/// <summary>
/// One live sign-in session. <c>Id</c> is what <c>DELETE /api/auth/sessions/{id}</c> takes, and stays
/// the same for the life of the session. <c>CreatedAt</c> is the sign-in, <c>LastUsedAt</c> the most
/// recent refresh and <c>IpAddress</c> where that came from. <c>ExpiresAt</c> is when it ends if not
/// used again, never later than the session's absolute lifetime. <c>IsCurrent</c> marks the session
/// the request was made from.
/// </summary>
public record SessionResponse(Guid Id, DateTime CreatedAt, DateTime LastUsedAt, DateTime ExpiresAt, string? IpAddress, bool IsCurrent = false);

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
