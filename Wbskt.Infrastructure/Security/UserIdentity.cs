namespace Wbskt.Infrastructure.Security;

public record UserIdentity
{
    public int UserId { get; init; }

    /// <summary>
    /// The user's public reference: the access token's <c>sub</c> (see <see cref="WbsktClaims"/>). Null
    /// only for an identity made without a token, as tests do; it is used to say who did something,
    /// never to authorize.
    /// </summary>
    public Guid? UserRefId { get; init; }

    public UserIdentity(int userId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        UserId = userId;
    }
}
