using System.Data;
using Microsoft.Data.SqlClient;
using Webskt.Common.Data;
using Webskt.Management.Host.Models;
using Webskt.Common.Abstraction.Exceptions;

namespace Webskt.Management.Host.Providers;

public class RegistrationPolicyProvider : BaseSqlProvider, IRegistrationPolicyProvider
{
    public RegistrationPolicyProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> FindByRefIdAsync(Guid refId)
    {
        var result = await ExecuteScalarAsync<int>("dbo.RegistrationPolicy_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", refId);
        });

        if (result <= 0)
        {
            throw new NotFoundException($"Policy with RefId {refId} not found.");
        }

        return result;
    }

    public async Task<RegistrationPolicy> GetByRefIdAsync(Guid refId)
    {
        return await ExecuteSingleAsync(
            "dbo.RegistrationPolicy_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapPolicy,
            new NotFoundException($"Policy with RefId {refId} not found.")
        );
    }

    public async Task<RegistrationPolicy> GetByPinAsync(string pin)
    {
        return await ExecuteSingleAsync(
            "dbo.RegistrationPolicy_GetBy_Pin",
            p => p.AddWithValue("@Pin", pin),
            MapPolicy,
            new SecurityException("Invalid registration PIN.")
        );
    }

    public async Task<IReadOnlyCollection<RegistrationPolicy>> GetAllAsync()
    {
        return await ExecuteCollectionAsync(
            "dbo.RegistrationPolicy_GetAll",
            null,
            MapPolicy
        );
    }

    public async Task<RegistrationPolicy> InsertAsync(RegistrationPolicyRequest request)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.RegistrationPolicy_Create", p =>
        {
            p.AddWithValue("@Name", request.Name);
            p.AddWithValue("@MaxClients", request.MaxClients ?? (object)DBNull.Value);
            p.AddWithValue("@AutoApproval", request.AutoApproval);
            
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
            p.Add("@Pin", SqlDbType.NVarChar, 10).Direction = ParameterDirection.Output;
        });

        var refId = (Guid)parameters["@RefId"].Value;

        // Fetch the full record to return complete data (including CreatedAt)
        return await GetByRefIdAsync(refId);
    }

    private static RegistrationPolicy MapPolicy(SqlDataReader reader)
    {
        return new RegistrationPolicy
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            Pin = reader.GetString(reader.GetOrdinal("Pin")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            MaxClients = reader.IsDBNull(reader.GetOrdinal("MaxClients")) ? null : reader.GetInt32(reader.GetOrdinal("MaxClients")),
            AutoApproval = reader.GetBoolean(reader.GetOrdinal("AutoApproval")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
