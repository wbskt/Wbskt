using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.Common.Data;
using Wbskt.Foundation.Abstraction.Exceptions;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class RegistrationPolicyProvider : BaseSqlProvider, IRegistrationPolicyProvider
{
    public RegistrationPolicyProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.RegistrationPolicy_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        }, cancellationToken);
    }

    public async Task<RegistrationPolicy> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.RegistrationPolicy_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapPolicy,
            new NotFoundException($"Policy with RefId {refId} not found."),
            cancellationToken
        );
    }

    public async Task<RegistrationPolicy> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.RegistrationPolicy_GetBy_Id",
            p => p.AddWithValue("@Id", id),
            MapPolicy,
            new NotFoundException($"Policy with Id {id} not found."),
            cancellationToken
        );
    }

    public async Task<RegistrationPolicy> GetByPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.RegistrationPolicy_GetBy_Pin",
            p => p.AddWithValue("@Pin", pin),
            MapPolicy,
            new SecurityException("Invalid registration PIN."),
            cancellationToken
        );
    }

    public async Task<IPagedList<RegistrationPolicy>> GetAllAsync(int workSpaceId, bool? autoApproval, string? name,
        int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.RegistrationPolicy_GetAll",
            p =>
            {
                p.AddWithValue("@WorkSpaceId", workSpaceId);
                p.AddWithValue("@AutoApproval", (object?)autoApproval ?? DBNull.Value);
                p.AddWithValue("@Name", (object?)name ?? DBNull.Value);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapPolicy,
            cancellationToken
        );
    }

    public async Task<RegistrationPolicy> InsertAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.RegistrationPolicy_Create", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@Name", request.Name);
            p.AddWithValue("@MaxClients", request.MaxClients ?? (object)DBNull.Value);
            p.AddWithValue("@AutoApproval", request.AutoApproval);
            
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
            p.Add("@Pin", SqlDbType.NVarChar, 10).Direction = ParameterDirection.Output;
        }, cancellationToken);

        var refId = (Guid)parameters["@RefId"].Value;

        // Fetch the full record to return complete data (including CreatedAt)
        return await GetByRefIdAsync(refId, cancellationToken);
    }

    public async Task UpdateAsync(int workspaceId, int id, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.RegistrationPolicy_Update", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@Id", id);
            p.AddWithValue("@Name", request.Name);
            p.AddWithValue("@MaxClients", (object?)request.MaxClients ?? DBNull.Value);
            p.AddWithValue("@AutoApproval", request.AutoApproval);
            p.AddWithValue("@IsEnabled", request.IsEnabled);
        }, cancellationToken);
    }

    public async Task DisableAsync(int workspaceId, int id, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.RegistrationPolicy_Disable", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@Id", id);
        }, cancellationToken);
    }

    private static RegistrationPolicy MapPolicy(SqlDataReader reader)
    {
        return new RegistrationPolicy
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            WorkspaceId = reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            Pin = reader.GetString(reader.GetOrdinal("Pin")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            MaxClients = reader.IsDBNull(reader.GetOrdinal("MaxClients")) ? null : reader.GetInt32(reader.GetOrdinal("MaxClients")),
            AutoApproval = reader.GetBoolean(reader.GetOrdinal("AutoApproval")),
            IsEnabled = reader.GetBoolean(reader.GetOrdinal("IsEnabled")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
