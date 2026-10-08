using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Management.Host.Providers;

internal sealed class RegistrationPolicyProvider : BaseSqlProvider, IRegistrationPolicyProvider
{
    private const int MaxPinAttempts = 3;

    public RegistrationPolicyProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.RegistrationPolicy_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        }, cancellationToken);
    }

    public async Task<RegistrationPolicy?> FindByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        return await ExecuteFindAsync(
            "dbo.RegistrationPolicy_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapPolicy,
            cancellationToken
        );
    }

    public async Task<RegistrationPolicy?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await ExecuteFindAsync(
            "dbo.RegistrationPolicy_GetBy_Id",
            p => p.AddWithValue("@Id", id),
            MapPolicy,
            cancellationToken
        );
    }

    public async Task<RegistrationPolicy?> FindByPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        return await ExecuteFindAsync(
            "dbo.RegistrationPolicy_GetBy_Pin",
            p => p.AddWithValue("@Pin", pin),
            MapPolicy,
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
        // PINs are unique across the platform. A clash at this length is vanishingly unlikely, but it
        // is a unique-key failure rather than a bad request, so a fresh PIN is drawn and tried again.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var parameters = await ExecuteNonQueryAsync("dbo.RegistrationPolicy_Create", p =>
                {
                    p.AddWithValue("@WorkspaceId", workspaceId);
                    p.AddWithValue("@Name", request.Name);
                    p.AddWithValue("@MaxClients", request.MaxClients ?? (object)DBNull.Value);
                    p.AddWithValue("@AutoApproval", request.AutoApproval);
                    p.Add("@Pin", SqlDbType.NVarChar, 20).Value = RegistrationPins.Generate();

                    p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
                    p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
                }, cancellationToken);

                var refId = (Guid)parameters["@RefId"].Value;

                // Fetch the full record to return complete data (including CreatedAt)
                return await FindByRefIdAsync(refId, cancellationToken)
                    ?? throw new InvalidOperationException($"Policy {refId} was not found right after it was inserted.");
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627 && attempt < MaxPinAttempts)
            {
                // Drawn PIN already taken; the next iteration draws another.
            }
        }
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

    public async Task<RegistrationPolicy> RotatePinAsync(int workspaceId, int id, CancellationToken cancellationToken = default)
    {
        // Same retry as InsertAsync: PINs are unique across the platform.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ExecuteNonQueryAsync("dbo.RegistrationPolicy_UpdatePin", p =>
                {
                    p.AddWithValue("@WorkspaceId", workspaceId);
                    p.AddWithValue("@Id", id);
                    p.Add("@Pin", SqlDbType.NVarChar, 20).Value = RegistrationPins.Generate();
                }, cancellationToken);

                return await FindByIdAsync(id, cancellationToken)
                    ?? throw new InvalidOperationException($"Policy {id} was not found right after its PIN was rotated.");
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627 && attempt < MaxPinAttempts)
            {
                // Drawn PIN already taken; the next iteration draws another.
            }
        }
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
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            RegisteredClientCount = reader.GetInt32(reader.GetOrdinal("RegisteredClientCount")),
            ConnectedClientCount = reader.GetInt32(reader.GetOrdinal("ConnectedClientCount"))
        };
    }
}
