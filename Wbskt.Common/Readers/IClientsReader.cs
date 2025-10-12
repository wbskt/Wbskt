using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

/// <summary>
/// Defines the contract for reading client data, potentially from a cache.
/// </summary>
public interface IClientsReader
{
    /// <summary>
    /// Gets a single client by its public reference ID.
    /// </summary>
    /// <param name="refId">The public reference ID of the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The client record, or null if not found.</returns>
    Task<ClientRecord?> GetByRefAsync(Guid refId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all clients belonging to a specific user.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of client records.</returns>
    Task<List<ClientRecord>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken);
}
