namespace Wbskt.Auth.Models;

/// <summary>
/// A proof-of-work puzzle that <c>POST /api/auth/register</c> wants solved. Find a
/// <c>solution</c> for which <see cref="SignUpProofOfWork.Satisfies"/> holds, and send it with
/// <see cref="Challenge"/> in the <see cref="RegisterRequest"/>.
/// </summary>
/// <param name="Challenge">Opaque and signed by the auth host. Send it back unchanged.</param>
/// <param name="Difficulty">How many leading zero bits the SHA-256 of <c>challenge:solution</c> needs.</param>
/// <param name="ExpiresAt">After this the challenge is refused; fetch a new one.</param>
public record SignUpChallengeResponse(string Challenge, int Difficulty, DateTimeOffset ExpiresAt);
