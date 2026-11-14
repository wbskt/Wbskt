using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;

namespace Wbskt.Auth.Host.Providers;

internal sealed class OutboxWriter(IConfiguration configuration) : BaseSqlProvider(configuration, "AuthDBConnection"), IOutboxWriter
{
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync("dbo.OutboxMessage_Insert", p =>
        {
            p.AddWithValue("@MessageType", message.MessageType);
            p.AddWithValue("@Payload", message.Payload);
        }, cancellationToken);
    }
}
