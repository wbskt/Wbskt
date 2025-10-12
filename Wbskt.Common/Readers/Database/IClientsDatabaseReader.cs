using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

/// <summary>
/// Defines the contract for reading all client data directly from the database.
/// </summary>
internal interface IClientsDatabaseReader
{
    /// <summary>
    /// Gets all clients from the database.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of all client records.</returns>
    Task<List<ClientRecord>> GetAllAsync(CancellationToken cancellationToken);
}
