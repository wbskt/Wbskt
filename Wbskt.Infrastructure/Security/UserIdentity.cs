namespace Wbskt.Infrastructure.Security;

public record UserIdentity
{
    public int UserId { get; init; }

    public UserIdentity(int userId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        UserId = userId;
    }
}
