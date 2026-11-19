using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>A person deleting their own account, and the sweep of accounts never confirmed.</summary>
public interface IAccountDeletionService
{
    /// <summary>
    /// Deletes the signed-in user's account after checking their password. Tenants only they belong
    /// to are deleted with their workspaces; they leave every other tenant, unless they are its only
    /// administrator, which refuses the whole deletion. Every session ends, and the account's name and
    /// address are anonymised. See <c>dbo.User_Delete</c>.
    /// </summary>
    Task<Result> DeleteAccountAsync(int userId, string password, string ipAddress, string? deviceToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an account whose email was never confirmed, the same way, if it still was not, was
    /// created before <paramref name="createdBeforeUtc"/>, never signed in and shares no tenant.
    /// Returns false, deleting nothing, when that no longer holds.
    /// </summary>
    Task<bool> DeleteUnconfirmedAsync(int userId, DateTime createdBeforeUtc, CancellationToken cancellationToken = default);
}
