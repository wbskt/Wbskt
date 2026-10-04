using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Management.Host.Providers;

internal sealed class ClientProvider : BaseSqlProvider, IClientProvider
{
    public ClientProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteScalarAsync<int>("dbo.Client_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        }, cancellationToken);

        return result;
    }

    public async Task<int> GetRegisteredCountByPolicyIdAsync(int policyId, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteScalarAsync<int>("dbo.Client_GetCountBy_PolicyId", p =>
        {
            p.AddWithValue("@PolicyId", policyId);
        }, cancellationToken);

        return result;
    }

    public async Task<Client> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.Client_GetBy_Id",
            p => p.AddWithValue("@Id", id),
            MapClient,
            new NotFoundException($"Client with Id {id} not found."),
            cancellationToken
        );
    }

    public async Task<IPagedList<Client>> GetAllAsync(int workspaceId, ClientStatus? status, string? name, int skip,
        int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.Client_GetAll",
            p =>
            {
                p.AddWithValue("@Status", (object?)status ?? DBNull.Value);
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@Name", (object?)name ?? DBNull.Value);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapClient,
            cancellationToken
        );
    }

    public async Task<IPagedList<Client>> GetByPolicyIdAsync(int workspaceId, int policyId, ClientStatus? status,
        string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.Client_GetBy_PolicyId",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@PolicyId", policyId);
                p.AddWithValue("@Status", (object?)status ?? DBNull.Value);
                p.AddWithValue("@Name", (object?)name ?? DBNull.Value);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapClient,
            cancellationToken
        );
    }

    public async Task<Client> InsertClientAsync(int workspaceId, int policyId, string name, byte[] secretHash,
        ClientStatus status, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Client_Create", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@PolicyId", policyId);
            p.AddWithValue("@Name", name);
            p.Add("@SecretHash", SqlDbType.VarBinary, ClientCredential.SecretHashBytes).Value = secretHash;
            p.AddWithValue("@Status", (byte)status);
            
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        }, cancellationToken);

        var refId = (Guid)parameters["@RefId"].Value;

        return await GetByRefIdAsync(refId, cancellationToken);
    }

    public async Task<ClientCredential> GetCredentialAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.Client_GetCredentialBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapCredential,
            new SecurityException("Invalid client credentials."),
            cancellationToken
        );
    }

    public async Task UpdateStatusAsync(int id, ClientStatus status, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Client_UpdateStatus", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@Status", (byte)status);
        }, cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, int workspaceId, CancellationToken cancellationToken = default)
    {
        var deleted = await ExecuteScalarAsync<int>("dbo.Client_Delete", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@WorkspaceId", workspaceId);
        }, cancellationToken);

        return deleted > 0;
    }

    public async Task<bool> UpdateSecretAsync(int id, int workspaceId, byte[] secretHash, CancellationToken cancellationToken = default)
    {
        var updated = await ExecuteScalarAsync<int>("dbo.Client_UpdateSecret", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.Add("@SecretHash", SqlDbType.VarBinary, ClientCredential.SecretHashBytes).Value = secretHash;
        }, cancellationToken);

        return updated > 0;
    }

    public async Task UpdatePresenceAsync(int id, bool isConnected, DateTime lastActivityAt, string? hostId = null, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Client_UpdatePresence", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@IsConnected", isConnected);
            // DateTime2 keeps the full millisecond, so a connect and a disconnect a few ms apart stay in order.
            p.Add("@LastActivityAt", SqlDbType.DateTime2).Value = lastActivityAt;
            p.AddWithValue("@HostId", (object?)hostId ?? DBNull.Value);
        }, cancellationToken);
    }

    public async Task ResetAllPresenceAsync(string? hostId = null, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Client_ResetAllPresence", p =>
        {
            p.AddWithValue("@HostId", (object?)hostId ?? DBNull.Value);
        }, cancellationToken);
    }

    public async Task<ClientDetail> GetDetailByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.Client_GetDetailBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapClientDetail,
            new NotFoundException($"Client with RefId {refId} not found."),
            cancellationToken
        );
    }

    public async Task UpdateNameAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Client_UpdateName", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@Name", name);
        }, cancellationToken);
    }

    public async Task UpdateRttAsync(int id, int lastRttMs, DateTime measuredAt, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Client_UpdateRtt", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@LastRttMs", lastRttMs);
            p.AddWithValue("@RttMeasuredAt", measuredAt);
        }, cancellationToken);
    }

    public async Task UpsertCapabilitiesAsync(int clientId, string agentName, string agentVersion, string platform,
        string capabilitiesJson, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.ClientCapabilities_Upsert", p =>
        {
            p.AddWithValue("@ClientId", clientId);
            p.AddWithValue("@AgentName", agentName);
            p.AddWithValue("@AgentVersion", agentVersion);
            p.AddWithValue("@Platform", platform);
            p.AddWithValue("@CapabilitiesJson", capabilitiesJson);
        }, cancellationToken);
    }

    public async Task<StateVariableUpsert> UpsertStateVariableAsync(int clientId, string name, string dataType, string valueJson,
        CancellationToken cancellationToken = default)
    {
        // Returns the previous ValueJson (NULL on first report) so callers can detect changes.
        return await ExecuteSingleAsync("dbo.ClientStateVariable_Upsert", p =>
        {
            p.AddWithValue("@ClientId", clientId);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@DataType", dataType);
            p.AddWithValue("@ValueJson", valueJson);
        },
        reader => new StateVariableUpsert(
            reader.GetBoolean(reader.GetOrdinal("Stored")),
            reader.IsDBNull(reader.GetOrdinal("OldValueJson")) ? null : reader.GetString(reader.GetOrdinal("OldValueJson"))),
        new InvalidOperationException("ClientStateVariable_Upsert did not return a row."),
        cancellationToken);
    }

    public async Task<IReadOnlyCollection<ClientStateVariable>> GetStateVariablesAsync(int clientId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.ClientStateVariable_GetBy_ClientId",
            p => p.AddWithValue("@ClientId", clientId),
            MapStateVariable,
            cancellationToken
        );
    }

    private async Task<Client> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.Client_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapClient,
            new NotFoundException($"Client with RefId {refId} not found."),
            cancellationToken
        );
    }

    private static Client MapClient(SqlDataReader reader)
    {
        return new Client
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            WorkspaceId = reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            PolicyId = reader.GetInt32(reader.GetOrdinal("PolicyId")),
            PolicyRefId = reader.GetGuid(reader.GetOrdinal("PolicyRefId")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Status = (ClientStatus)reader.GetByte(reader.GetOrdinal("Status")),
            IsConnected = reader.GetBoolean(reader.GetOrdinal("IsConnected")),
            ConnectedAt = reader.IsDBNull(reader.GetOrdinal("ConnectedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ConnectedAt")),
            LastActivityAt = reader.IsDBNull(reader.GetOrdinal("LastActivityAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastActivityAt")),
            LastRttMs = reader.IsDBNull(reader.GetOrdinal("LastRttMs")) ? null : reader.GetInt32(reader.GetOrdinal("LastRttMs")),
            RttMeasuredAt = reader.IsDBNull(reader.GetOrdinal("RttMeasuredAt")) ? null : reader.GetDateTime(reader.GetOrdinal("RttMeasuredAt")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }

    private static ClientCredential MapCredential(SqlDataReader reader)
    {
        return new ClientCredential(
            MapClient(reader),
            (byte[])reader[reader.GetOrdinal("SecretHash")]);
    }

    private static ClientStateVariable MapStateVariable(SqlDataReader reader)
    {
        return new ClientStateVariable
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            ClientId = reader.GetInt32(reader.GetOrdinal("ClientId")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            DataType = reader.GetString(reader.GetOrdinal("DataType")),
            ValueJson = reader.GetString(reader.GetOrdinal("ValueJson")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
        };
    }

    private static ClientDetail MapClientDetail(SqlDataReader reader)
    {
        return new ClientDetail
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            WorkspaceId = reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            PolicyId = reader.GetInt32(reader.GetOrdinal("PolicyId")),
            PolicyRefId = reader.GetGuid(reader.GetOrdinal("PolicyRefId")),
            PolicyName = reader.GetString(reader.GetOrdinal("PolicyName")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Status = (ClientStatus)reader.GetByte(reader.GetOrdinal("Status")),
            IsConnected = reader.GetBoolean(reader.GetOrdinal("IsConnected")),
            ConnectedAt = reader.IsDBNull(reader.GetOrdinal("ConnectedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ConnectedAt")),
            LastActivityAt = reader.IsDBNull(reader.GetOrdinal("LastActivityAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastActivityAt")),
            LastRttMs = reader.IsDBNull(reader.GetOrdinal("LastRttMs")) ? null : reader.GetInt32(reader.GetOrdinal("LastRttMs")),
            RttMeasuredAt = reader.IsDBNull(reader.GetOrdinal("RttMeasuredAt")) ? null : reader.GetDateTime(reader.GetOrdinal("RttMeasuredAt")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            AgentName = reader.IsDBNull(reader.GetOrdinal("AgentName")) ? null : reader.GetString(reader.GetOrdinal("AgentName")),
            AgentVersion = reader.IsDBNull(reader.GetOrdinal("AgentVersion")) ? null : reader.GetString(reader.GetOrdinal("AgentVersion")),
            Platform = reader.IsDBNull(reader.GetOrdinal("Platform")) ? null : reader.GetString(reader.GetOrdinal("Platform")),
            CapabilitiesJson = reader.IsDBNull(reader.GetOrdinal("CapabilitiesJson")) ? null : reader.GetString(reader.GetOrdinal("CapabilitiesJson")),
            CapabilitiesUpdatedAt = reader.IsDBNull(reader.GetOrdinal("CapabilitiesUpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CapabilitiesUpdatedAt"))
        };
    }
}
