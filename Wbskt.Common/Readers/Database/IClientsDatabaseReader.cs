using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

/// <summary>
/// Defines the contract for reading all client data directly from the database.
/// </summary>
internal interface IClientsDatabaseReader
{
    /// <summary>
    /// Gets all clients from the database modified after a certain time.
    /// </summary>
    /// <param name="lastModified">The timestamp to query from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of client records.</returns>
    Task<List<ClientRecord>> GetAllAsync(DateTime lastModified, CancellationToken cancellationToken);
}
