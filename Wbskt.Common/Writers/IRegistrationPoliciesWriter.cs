using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

/// <summary>
/// Defines the contract for writing registration policy data.
/// </summary>
public interface IRegistrationPoliciesWriter
{
    /// <summary>
    /// Inserts a new registration policy.
    /// </summary>
    /// <param name="policy">The registration policy record to insert.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ID of the newly created policy.</returns>
    Task<int> InsertAsync(RegistrationPolicyRecord policy, CancellationToken cancellationToken);
}
