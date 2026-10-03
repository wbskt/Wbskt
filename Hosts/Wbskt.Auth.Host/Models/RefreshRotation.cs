namespace Wbskt.Auth.Host.Models;

/// <summary>What <c>dbo.RefreshToken_Rotate</c> did with a presented refresh token.</summary>
public enum RefreshRotationOutcome
{
    Rotated,
    Unknown,
    Replayed,
    Expired,
    UserInactive,
    Raced
}

/// <summary>
/// The result of one refresh-token exchange. <see cref="User"/> is set only when the outcome is
/// <see cref="RefreshRotationOutcome.Rotated"/>, and carries just what an access token needs.
/// </summary>
public sealed record RefreshRotation(RefreshRotationOutcome Outcome, int? UserId, User? User);
