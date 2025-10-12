using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

/// <summary>
/// Defines the contract for reading registration policy data, potentially from a cache.
/// </summary>
public interface IRegistrationPoliciesReader
{
    /// <summary>
    /// Gets all registration policies for a specific user.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of registration policy records.</returns>
    Task<List<RegistrationPolicyRecord>> GetAllAsync(int userId, CancellationToken cancellationToken);
    Task<RegistrationPolicyRecord?> GetByRefIdAsync(int userId, Guid refId, CancellationToken cancellationToken);
}
