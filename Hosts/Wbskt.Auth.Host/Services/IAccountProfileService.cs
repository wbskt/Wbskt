using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>Changing the signed-in person's username and email address.</summary>
public interface IAccountProfileService
{
    /// <summary>
    /// Renames the account, at most once every 30 days, and returns a fresh access token for
    /// <paramref name="sessionId"/> carrying the new name. The refresh token is unchanged, so the
    /// response has none.
    /// </summary>
    Task<Result<LoginResponse>> ChangeUsernameAsync(int userId, Guid? sessionId, string username, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// After checking the password, mails a confirm link to <paramref name="newEmail"/>. Nothing
    /// changes until it is followed; a new request replaces one still pending.
    /// </summary>
    Task<Result> RequestEmailChangeAsync(int userId, string password, string newEmail, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Follows a confirm link: switches the address, ends every session, and mails the old address a
    /// link that undoes it for 7 days. When <paramref name="callerUserId"/> is the account that changed,
    /// returns a fresh session for the caller; otherwise null, and the person signs in again.
    /// </summary>
    Task<Result<LoginResponse?>> ConfirmEmailChangeAsync(string token, int? callerUserId, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Follows the undo link mailed to the old address: puts it back and ends every session.</summary>
    Task<Result> RevertEmailChangeAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
}
