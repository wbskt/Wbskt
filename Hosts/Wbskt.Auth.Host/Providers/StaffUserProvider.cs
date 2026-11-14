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
}
