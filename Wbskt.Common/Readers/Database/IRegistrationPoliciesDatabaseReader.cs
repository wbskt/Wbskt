using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

/// <summary>
/// Defines the contract for reading registration policy data directly from the database.
/// </summary>
public interface IRegistrationPoliciesDatabaseReader
{
    /// <summary>
    /// Gets all registration policies from the database modified after a certain time.
    /// </summary>
    /// <param name="lastModified">The timestamp to query from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of registration policy records.</returns>
    Task<List<RegistrationPolicyRecord>> GetAllAsync(DateTime lastModified, CancellationToken cancellationToken);
    Task<RegistrationPolicyRecord?> GetByRefIdAsync(int userId, Guid refId, CancellationToken cancellationToken);
}
