using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Providers;

internal sealed class TenantProvider : BaseSqlProvider, ITenantProvider
{
    public TenantProvider(IConfiguration configuration) : base(configuration, "AuthDBConnection") { }

    public async Task<int> FindTenantIdByRefIdForUserAsync(Guid tenantRef, int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.Tenant_FindBy_RefIdForUser", p =>
        {
            p.AddWithValue("@RefId", tenantRef);
            p.AddWithValue("@UserId", userId);
        }, cancellationToken);
    }

    public async Task<int> FindUserIdByRefIdInTenantAsync(Guid userRef, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.User_FindBy_RefIdInTenant", p =>
        {
            p.AddWithValue("@RefId", userRef);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Tenant>> GetTenantsForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Tenant_GetForUser",
            p => p.AddWithValue("@UserId", userId),
            r => new Tenant
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                RefId = r.GetGuid(r.GetOrdinal("RefId")),
                Name = r.GetString(r.GetOrdinal("Name")),
                Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
            },
            cancellationToken
        );
    }

    public async Task<Guid> CreateTenantAsync(string name, string? description, int ownerUserId, string workspaceName, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Tenant_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            p.AddWithValue("@OwnerUserId", ownerUserId);
            p.AddWithValue("@WorkspaceName", workspaceName);
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
            p.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (Guid)parameters["@RefId"].Value;
    }

    public async Task UpdateTenantAsync(int tenantId, string name, string? description, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Tenant_Update", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", (object?)description ?? DBNull.Value);
        }, cancellationToken);
    }

    public async Task<IPagedList<TenantMemberResponse>> GetTenantMembersAsync(int tenantId, string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.TenantMember_GetAll",
            p =>
            {
                p.AddWithValue("@TenantId", tenantId);
                p.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapTenantMember,
            cancellationToken
        );
    }

    public async Task RemoveTenantMemberAsync(int tenantId, int userId, int newOwnerUserId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.TenantMember_Remove", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@NewOwnerUserId", newOwnerUserId);
        }, cancellationToken);
    }

    public async Task SetMemberSuspendedAsync(int tenantId, int userId, bool isSuspended, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.TenantMember_SetSuspended", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@IsSuspended", isSuspended);
        }, cancellationToken);
    }

    public async Task<CreatedInvitation> CreateInvitationAsync(int tenantId, string email, int? roleId, byte[] tokenHash, DateTime expiresAt, int invitedByUserId, IReadOnlyCollection<int> workspaceIds, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.TenantInvitation_Create", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Email", email);
            p.AddWithValue("@RoleId", (object?)roleId ?? DBNull.Value);
            p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
            p.AddWithValue("@ExpiresAt", expiresAt);
            p.AddWithValue("@InvitedByUserId", invitedByUserId);
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
            p.Add("@TenantName", SqlDbType.NVarChar, 100).Direction = ParameterDirection.Output;
            p.AddWithValue("@WorkspaceIds", workspaceIds.Count == 0 ? DBNull.Value : string.Join(',', workspaceIds));
        }, cancellationToken);

        return new CreatedInvitation((Guid)parameters["@RefId"].Value, (string)parameters["@TenantName"].Value);
    }

    public async Task<InvitationLookup> GetInvitationByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.TenantInvitation_GetBy_TokenHash",
            p => p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash,
            r => new InvitationLookup(
                r.GetGuid(r.GetOrdinal("RefId")),
                r.GetGuid(r.GetOrdinal("TenantRefId")),
                r.GetString(r.GetOrdinal("TenantName")),
                r.GetString(r.GetOrdinal("Email")),
                r.GetDateTime(r.GetOrdinal("ExpiresAt")),
                r.GetBoolean(r.GetOrdinal("IsLive"))),
            new SecurityException("Invitation not found."),
            cancellationToken
        );
    }

    public async Task<int> AcceptInvitationAsync(byte[] tokenHash, int userId, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.TenantInvitation_Accept", p =>
        {
            p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
            p.AddWithValue("@UserId", userId);
            p.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (int)parameters["@TenantId"].Value;
    }

    public async Task<string?> RevokeInvitationAsync(Guid invitationRef, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<string>("dbo.TenantInvitation_Revoke", p =>
        {
            p.AddWithValue("@RefId", invitationRef);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task<IPagedList<InvitationResponse>> GetInvitationsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.TenantInvitation_GetAll",
            p =>
            {
                p.AddWithValue("@TenantId", tenantId);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapInvitation,
            cancellationToken
        );
    }

    private static InvitationResponse MapInvitation(SqlDataReader reader)
    {
        return new InvitationResponse(
            reader.GetGuid(reader.GetOrdinal("RefId")),
            reader.GetString(reader.GetOrdinal("Email")),
            reader.IsDBNull(reader.GetOrdinal("RoleRefId")) ? null : reader.GetGuid(reader.GetOrdinal("RoleRefId")),
            reader.IsDBNull(reader.GetOrdinal("RoleName")) ? null : reader.GetString(reader.GetOrdinal("RoleName")),
            reader.GetDateTime(reader.GetOrdinal("ExpiresAt")),
            reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            reader.IsDBNull(reader.GetOrdinal("WorkspaceRefIds"))
                ? []
                : reader.GetString(reader.GetOrdinal("WorkspaceRefIds")).Split(',').Select(Guid.Parse).ToArray()
        );
    }

    private static TenantMemberResponse MapTenantMember(SqlDataReader reader)
    {
        return new TenantMemberResponse(
            reader.GetGuid(reader.GetOrdinal("RefId")),
            reader.GetString(reader.GetOrdinal("Username")),
            reader.GetString(reader.GetOrdinal("Email")),
            reader.GetBoolean(reader.GetOrdinal("IsActive")),
            reader.GetBoolean(reader.GetOrdinal("IsSuspended"))
        );
    }
}
