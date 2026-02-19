using Webskt.Common.Abstraction.Models.Management;
using System.Data;
using Microsoft.Data.SqlClient;
using Webskt.Common.Data;
using Webskt.Management.Host.Models;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Models;

namespace Webskt.Management.Host.Providers;

internal sealed class ClientProvider : BaseSqlProvider, IClientProvider
{
    public ClientProvider(IConfiguration configuration) : base(configuration)
    {
    }

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

    public async Task<IPagedList<Client>> GetAllAsync(ClientStatus? status, string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.Client_GetAll",
            p =>
            {
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

    public async Task<IPagedList<Client>> GetByPolicyIdAsync(int policyId, ClientStatus? status, string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.Client_GetBy_PolicyId",
            p =>
            {
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

    public async Task<Client> InsertClientAsync(int policyId, string name, string secret, ClientStatus status, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Client_Create", p =>
        {
            p.AddWithValue("@PolicyId", policyId);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Secret", secret);
            p.AddWithValue("@Status", (byte)status);
            
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        }, cancellationToken);

        var refId = (Guid)parameters["@RefId"].Value;

        return await GetByRefIdAsync(refId, cancellationToken);
    }

    public async Task<Client> VerifyAsync(Guid refId, string secret, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.Client_Verify",
            p => 
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@Secret", secret);
            },
            MapClient,
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

    public async Task UpdatePresenceAsync(int id, bool isConnected, DateTime lastActivityAt, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Client_UpdatePresence", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@IsConnected", isConnected);
            p.AddWithValue("@LastActivityAt", lastActivityAt);
        }, cancellationToken);
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
            Secret = reader.GetString(reader.GetOrdinal("Secret")),
            Status = (ClientStatus)reader.GetByte(reader.GetOrdinal("Status")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
