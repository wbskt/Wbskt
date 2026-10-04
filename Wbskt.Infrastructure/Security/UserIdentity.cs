namespace Wbskt.Infrastructure.Security;

public record UserIdentity
{
    public int UserId { get; init; }

    /// <summary>
    /// The user's public reference, from the access token's <c>uref</c> claim. Null for a token issued
    /// before the claim existed; it is only used to say who did something, never to authorize.
    /// </summary>
    public Guid? UserRefId { get; init; }

    public UserIdentity(int userId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        UserId = userId;
    }
}
