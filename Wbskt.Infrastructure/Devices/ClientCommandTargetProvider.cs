using Microsoft.Extensions.Configuration;

namespace Wbskt.Infrastructure.Devices;

/// <summary>A client as a command sees it: whose it is, and which socket host holds its connection.</summary>
public sealed record ClientCommandTarget(int ClientId, int WorkspaceId, bool IsConnected, string? HostId);

public interface IClientCommandTargetProvider
{
    /// <summary>The client's command target, or null when there is no such client.</summary>
    Task<ClientCommandTarget?> FindAsync(Guid clientRefId, CancellationToken ct);
}

internal sealed class ClientCommandTargetProvider : BaseSqlProvider, IClientCommandTargetProvider
{
    public ClientCommandTargetProvider(IConfiguration configuration) : base(configuration) { }

    public Task<ClientCommandTarget?> FindAsync(Guid clientRefId, CancellationToken ct)
    {
        return ExecuteFindAsync(
            "dbo.Client_GetCommandTargetBy_RefId",
            p => p.AddWithValue("@RefId", clientRefId),
            reader =>
            {
                var hostOrdinal = reader.GetOrdinal("ConnectedHostId");
                return new ClientCommandTarget(
                    reader.GetInt32(reader.GetOrdinal("Id")),
                    reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
                    reader.GetBoolean(reader.GetOrdinal("IsConnected")),
                    reader.IsDBNull(hostOrdinal) ? null : reader.GetString(hostOrdinal));
            },
            ct);
    }
}
