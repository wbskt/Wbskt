using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Providers;

internal sealed class StaffUserProvider(IConfiguration configuration) : BaseSqlProvider(configuration, "AuthDBConnection"), IStaffUserProvider
{
    public Task ClearLockoutAsync(int userId, CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync("dbo.User_ClearLockout", p => p.AddWithValue("@Id", userId), cancellationToken);
    }

    public Task SetActiveAsync(int userId, bool isActive, string revokedByIp, CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync("dbo.User_StaffSetActive", p =>
        {
            p.AddWithValue("@Id", userId);
            p.AddWithValue("@IsActive", isActive);
            p.AddWithValue("@RevokedByIp", revokedByIp);
        }, cancellationToken);
    }

    public Task CorrectUnverifiedEmailAsync(int userId, string email, byte[] tokenHash, DateTime expiresAt, CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync("dbo.User_StaffCorrectEmail", p =>
        {
            p.AddWithValue("@Id", userId);
            p.AddWithValue("@Email", email);
            p.AddWithValue("@TokenHash", tokenHash);
            p.AddWithValue("@ExpiresAt", expiresAt);
        }, cancellationToken);
    }

    public Task<int> FindTenantIdByRefIdAsync(Guid tenantRef, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<int>("dbo.Tenant_FindBy_RefId", p => p.AddWithValue("@RefId", tenantRef), cancellationToken);
    }
}
