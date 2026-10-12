using Microsoft.Extensions.Configuration;

namespace Wbskt.Infrastructure.Events.Outbox;

/// <summary>Reads and clears a database's <c>dbo.OutboxMessages</c>.</summary>
public interface IOutboxStore
{
    Task<IReadOnlyCollection<PendingOutboxMessage>> GetPendingAsync(int take, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}

internal sealed class SqlOutboxStore(IConfiguration configuration, string connectionStringName)
    : BaseSqlProvider(configuration, connectionStringName), IOutboxStore
{
    public Task<IReadOnlyCollection<PendingOutboxMessage>> GetPendingAsync(int take, CancellationToken cancellationToken)
    {
        return ExecuteCollectionAsync(
            "dbo.OutboxMessage_GetPending",
            p => p.AddWithValue("@Take", take),
            r => new PendingOutboxMessage(
                r.GetInt64(r.GetOrdinal("Id")),
                r.GetString(r.GetOrdinal("MessageType")),
                r.GetString(r.GetOrdinal("Payload")),
                r.GetDateTime(r.GetOrdinal("CreatedAt"))),
            cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync("dbo.OutboxMessage_Delete", p => p.AddWithValue("@Id", id), cancellationToken);
    }
}
