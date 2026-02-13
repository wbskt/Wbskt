using Webskt.Common.Abstraction.Models.Management;
using System.Data;
using Microsoft.Data.SqlClient;
using Webskt.Common.Data;
using Webskt.Management.Host.Models;
using Webskt.Common.Abstraction.Exceptions;

namespace Webskt.Management.Host.Providers;

internal sealed class ClientProvider : BaseSqlProvider, IClientProvider
{
    public ClientProvider(IConfiguration configuration) : base(configuration)
    {
    }

    public async Task<int> FindByReferenceIdAsync(Guid referenceId)
    {
        var result = await ExecuteScalarAsync<int>("dbo.Client_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        });

        return result;
    }

    public async Task<int> GetRegisteredCountByPolicyIdAsync(int policyId)
    {
        var result = await ExecuteScalarAsync<int>("dbo.Client_GetCountBy_PolicyId", p =>
        {
            p.AddWithValue("@PolicyId", policyId);
        });

        return result;
    }

    public async Task<IReadOnlyCollection<Client>> GetAllAsync()
    {
        return await ExecuteCollectionAsync(
            "dbo.Client_GetAll",
            null,
            MapClient
        );
    }

    public async Task<IReadOnlyCollection<Client>> GetByPolicyIdAsync(int policyId)
    {
        return await ExecuteCollectionAsync(
            "dbo.Client_GetBy_PolicyId",
            p => p.AddWithValue("@PolicyId", policyId),
            MapClient
        );
    }

    public async Task<Client> InsertClientAsync(int policyId, string name, string secret, ClientStatus status)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Client_Create", p =>
        {
            p.AddWithValue("@PolicyId", policyId);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Secret", secret);
            p.AddWithValue("@Status", (byte)status);
            
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        });

        var refId = (Guid)parameters["@RefId"].Value;

        return await GetByRefIdAsync(refId);
    }

    public async Task<Client> VerifyAsync(Guid refId, string secret)
    {
        return await ExecuteSingleAsync(
            "dbo.Client_Verify",
            p => 
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@Secret", secret);
            },
            MapClient,
            new SecurityException("Invalid client credentials.")
        );
    }

    public async Task UpdateStatusAsync(int id, ClientStatus status)
    {
        await ExecuteNonQueryAsync("dbo.Client_UpdateStatus", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@Status", (byte)status);
        });
    }

    private async Task<Client> GetByRefIdAsync(Guid refId)
    {
        return await ExecuteSingleAsync(
            "dbo.Client_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapClient,
            new NotFoundException($"Client with RefId {refId} not found.")
        );
    }

    private static Client MapClient(SqlDataReader reader)
    {
        return new Client
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            PolicyId = reader.GetInt32(reader.GetOrdinal("PolicyId")),
            PolicyRefId = reader.GetGuid(reader.GetOrdinal("PolicyRefId")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Secret = reader.GetString(reader.GetOrdinal("Secret")),
            Status = (ClientStatus)reader.GetByte(reader.GetOrdinal("Status")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
