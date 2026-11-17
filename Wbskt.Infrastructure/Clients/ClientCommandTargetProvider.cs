using Microsoft.Extensions.Configuration;
using Wbskt.Protocol;

namespace Wbskt.Infrastructure.Clients;

/// <summary>
/// A client as a command sees it: whose it is, which socket host holds its connection, and whether it
/// is registered (only a registered client can have a command wait for it).
/// </summary>
/// <param name="ConnectedAtUtc">When the socket host holding it saw it connect, on that host's clock.</param>
public sealed record ClientCommandTarget(int ClientId, int WorkspaceId, bool IsConnected, string? HostId, ClientStatus Status = ClientStatus.Registered, DateTime? ConnectedAtUtc = null);

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
                var connectedAtOrdinal = reader.GetOrdinal("ConnectedAt");
                return new ClientCommandTarget(
                    reader.GetInt32(reader.GetOrdinal("Id")),
                    reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
                    reader.GetBoolean(reader.GetOrdinal("IsConnected")),
                    reader.IsDBNull(hostOrdinal) ? null : reader.GetString(hostOrdinal),
                    (ClientStatus)reader.GetByte(reader.GetOrdinal("Status")),
                    reader.IsDBNull(connectedAtOrdinal) ? null : DateTime.SpecifyKind(reader.GetDateTime(connectedAtOrdinal), DateTimeKind.Utc));
            },
            ct);
    }
}
