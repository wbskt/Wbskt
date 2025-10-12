using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

/// <summary>
/// Defines the contract for writing client data.
/// </summary>
public interface IClientsWriter
{
    /// <summary>
    /// Creates or updates a client record in the database.
    /// </summary>
    /// <param name="client">The client record to upsert.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ID of the upserted client.</returns>
    Task<int> UpsertAsync(ClientRecord client, CancellationToken cancellationToken);
}
