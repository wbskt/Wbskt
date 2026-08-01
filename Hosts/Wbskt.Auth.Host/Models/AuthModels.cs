using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Host.Models;

public record LoginRequest(
    [property: Required]
    [property: EmailAddress]
    [property: StringLength(100)]
    string Email,

    [property: Required]
    [property: StringLength(128)]
    string Password);

public record LoginResponse(string AccessToken, string RefreshToken);

/// <summary>
/// Carries a refresh token for rotation or revocation. A record rather than a bare
/// <c>[FromBody] string</c> so the body is normal JSON and the endpoint documents itself.
/// </summary>
public record RefreshTokenRequest(
    [property: Required]
    [property: StringLength(255)]
    string RefreshToken);
