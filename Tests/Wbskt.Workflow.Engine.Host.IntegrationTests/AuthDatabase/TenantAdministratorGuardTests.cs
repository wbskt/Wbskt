using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// The last-administrator guard (THROW 50008), on every procedure that can take tenant-wide
/// users.manage away from someone. A tenant left with no administrator cannot be repaired through
/// the API, so each of these is a path to an unrecoverable tenant if the guard is missing.
/// </summary>
[Collection("SqlEdge")]
public sealed class TenantAdministratorGuardTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";
    private const int LastAdministrator = 50008;

    [SkippableFact]
    public async Task The_only_administrator_cannot_lose_their_Admin_role()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();

        await AssertRejectedAsync(() => ExecAsync("dbo.UserRole_Remove", ("@UserId", ownerId), ("@RoleId", adminRoleId), ("@TenantId", tenantId)));
        Assert.True(await IsAdministratorAsync(tenantId, ownerId));
    }

    [SkippableFact]
    public async Task An_administrator_can_lose_their_Admin_role_while_another_remains()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();
        int second = await AddMemberAsync(tenantId, adminRoleId);

        await ExecAsync("dbo.UserRole_Remove", ("@UserId", ownerId), ("@RoleId", adminRoleId), ("@TenantId", tenantId));

        Assert.False(await IsAdministratorAsync(tenantId, ownerId));
        Assert.True(await IsAdministratorAsync(tenantId, second));
    }

    [SkippableFact]
    public async Task The_Admin_role_cannot_be_deleted_from_under_the_only_administrator()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();

        await AssertRejectedAsync(() => ExecAsync("dbo.Role_Delete", ("@Id", adminRoleId), ("@TenantId", tenantId)));
        Assert.True(await IsAdministratorAsync(tenantId, ownerId));
    }

    [SkippableFact]
    public async Task Admin_cannot_be_stripped_of_users_manage_by_removal_or_deny()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();

        await AssertRejectedAsync(() => ExecAsync("dbo.RolePermission_Remove",
            ("@RoleId", adminRoleId), ("@PermissionSlug", "users.manage"), ("@TenantId", tenantId)));
        await AssertRejectedAsync(() => ExecAsync("dbo.RolePermission_Grant",
            ("@RoleId", adminRoleId), ("@PermissionSlug", "users.manage"), ("@IsDeny", true), ("@TenantId", tenantId)));

        Assert.True(await IsAdministratorAsync(tenantId, ownerId));
    }

    [SkippableFact]
    public async Task The_only_administrator_cannot_be_denied_users_manage_directly()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, _) = await CreateTenantAsync();

        await AssertRejectedAsync(() => ExecAsync("dbo.UserPermission_Grant",
            ("@UserId", ownerId), ("@PermissionSlug", "users.manage"), ("@IsDeny", true), ("@TenantId", tenantId)));
        Assert.True(await IsAdministratorAsync(tenantId, ownerId));
    }

    [SkippableFact]
    public async Task Removing_a_direct_grant_cannot_remove_the_only_administrator()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();
        int second = await AddMemberAsync(tenantId, roleId: null);

        // The second member administers only through a direct grant; the owner then steps down.
        await ExecAsync("dbo.UserPermission_Grant",
            ("@UserId", second), ("@PermissionSlug", "users.manage"), ("@IsDeny", false), ("@TenantId", tenantId));
        await ExecAsync("dbo.UserRole_Remove", ("@UserId", ownerId), ("@RoleId", adminRoleId), ("@TenantId", tenantId));

        await AssertRejectedAsync(() => ExecAsync("dbo.UserPermission_Remove",
            ("@UserId", second), ("@PermissionSlug", "users.manage"), ("@TenantId", tenantId)));
        Assert.True(await IsAdministratorAsync(tenantId, second));
    }

    [SkippableFact]
    public async Task The_only_administrator_cannot_be_deactivated()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();

        await AssertRejectedAsync(() => ExecAsync("dbo.User_SetActive", ("@Id", ownerId), ("@IsActive", false), ("@TenantId", tenantId)));
        Assert.True(await ScalarAsync<bool>("SELECT IsActive FROM dbo.Users WHERE Id = @p0", ownerId));

        // With a second administrator, deactivation goes through.
        await AddMemberAsync(tenantId, adminRoleId);
        await ExecAsync("dbo.User_SetActive", ("@Id", ownerId), ("@IsActive", false), ("@TenantId", tenantId));
        Assert.False(await ScalarAsync<bool>("SELECT IsActive FROM dbo.Users WHERE Id = @p0", ownerId));
    }

    /// <summary>
    /// The account is global, but the guard covers only the tenant the request is made in: every
    /// registered user administers a tenant of their own, so guarding all of them would refuse to
    /// deactivate nearly anyone. Tenant-scoped suspension is the fix for the wider blast radius.
    /// </summary>
    [SkippableFact]
    public async Task Deactivation_is_guarded_only_in_the_tenant_it_is_requested_in()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, _, _) = await CreateTenantAsync();
        var (_, elsewhereOwnerId, _) = await CreateTenantAsync();
        await ExecSqlAsync("INSERT INTO dbo.TenantMembers (TenantId, UserId) VALUES (@p0, @p1);", tenantId, elsewhereOwnerId);

        await ExecAsync("dbo.User_SetActive", ("@Id", elsewhereOwnerId), ("@IsActive", false), ("@TenantId", tenantId));

        Assert.False(await ScalarAsync<bool>("SELECT IsActive FROM dbo.Users WHERE Id = @p0", elsewhereOwnerId));
    }

    [SkippableFact]
    public async Task The_only_administrator_cannot_be_suspended()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();

        await AssertRejectedAsync(() => ExecAsync("dbo.TenantMember_SetSuspended",
            ("@TenantId", tenantId), ("@UserId", ownerId), ("@IsSuspended", true)));
        Assert.True(await IsAdministratorAsync(tenantId, ownerId));

        // With a second administrator the suspension goes through, and the suspended one stops counting.
        int second = await AddMemberAsync(tenantId, adminRoleId);
        await ExecAsync("dbo.TenantMember_SetSuspended", ("@TenantId", tenantId), ("@UserId", ownerId), ("@IsSuspended", true));
        Assert.False(await IsAdministratorAsync(tenantId, ownerId));

        await AssertRejectedAsync(() => ExecAsync("dbo.TenantMember_SetSuspended",
            ("@TenantId", tenantId), ("@UserId", second), ("@IsSuspended", true)));
    }

    [SkippableFact]
    public async Task Suspending_someone_outside_the_tenant_is_refused()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, _, _) = await CreateTenantAsync();
        var (_, outsiderId, _) = await CreateTenantAsync();

        var ex = await Assert.ThrowsAsync<SqlException>(() => ExecAsync("dbo.TenantMember_SetSuspended",
            ("@TenantId", tenantId), ("@UserId", outsiderId), ("@IsSuspended", true)));
        Assert.Equal(50016, ex.Number);
    }

    /// <summary>
    /// Suspension removes everything the member holds in that tenant and nothing elsewhere, and
    /// lifting it restores exactly what they had, since no assignment was touched.
    /// </summary>
    [SkippableFact]
    public async Task Suspension_closes_one_tenant_and_lifting_it_restores_access()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, _, adminRoleId) = await CreateTenantAsync();
        int member = await AddMemberAsync(tenantId, adminRoleId);
        int workspaceId = await ScalarAsync<int>("SELECT TOP 1 Id FROM dbo.Workspaces WHERE TenantId = @p0", tenantId);
        await ExecSqlAsync("INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId) VALUES (@p0, @p1);", workspaceId, member);

        // The member also owns a tenant of their own.
        int ownTenantId = await CreateTenantForAsync(member);
        int ownWorkspaceId = await ScalarAsync<int>("SELECT TOP 1 Id FROM dbo.Workspaces WHERE TenantId = @p0", ownTenantId);

        Assert.True(await HasAccessAsync(member, tenantId, workspaceId));

        await ExecAsync("dbo.TenantMember_SetSuspended", ("@TenantId", tenantId), ("@UserId", member), ("@IsSuspended", true));

        Assert.False(await HasAccessAsync(member, tenantId, workspaceId));
        Assert.True(await HasAccessAsync(member, ownTenantId, ownWorkspaceId));

        await ExecAsync("dbo.TenantMember_SetSuspended", ("@TenantId", tenantId), ("@UserId", member), ("@IsSuspended", false));

        Assert.True(await HasAccessAsync(member, tenantId, workspaceId));
    }

    /// <summary>
    /// An inactive account cannot sign in, so it cannot be the administrator that "remains".
    /// The guard used to count it, which let the last active administrator be removed.
    /// </summary>
    [SkippableFact]
    public async Task An_inactive_administrator_does_not_count_as_remaining()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();
        int dormant = await AddMemberAsync(tenantId, adminRoleId);
        await ExecSqlAsync("UPDATE dbo.Users SET IsActive = 0 WHERE Id = @p0;", dormant);

        await AssertRejectedAsync(() => ExecAsync("dbo.TenantMember_Remove",
            ("@TenantId", tenantId), ("@UserId", ownerId), ("@NewOwnerUserId", dormant)));
        await AssertRejectedAsync(() => ExecAsync("dbo.UserRole_Remove",
            ("@UserId", ownerId), ("@RoleId", adminRoleId), ("@TenantId", tenantId)));
    }

    /// <summary>
    /// The guard refuses making things worse, not changes in a tenant that already has no
    /// (countable) administrator — such as one administered only through a group.
    /// </summary>
    [SkippableFact]
    public async Task A_tenant_with_no_countable_administrator_is_not_frozen()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();
        // Set up the state directly, since the guard (correctly) refuses to create it.
        await ExecSqlAsync("""
            INSERT INTO dbo.UserPermissions (UserId, PermissionId, TenantId, WorkspaceId, IsDeny)
            SELECT @p0, P.Id, @p1, NULL, 1 FROM dbo.Permissions P WHERE P.Slug = 'users.manage';
            """, ownerId, tenantId);
        Assert.False(await IsAdministratorAsync(tenantId, ownerId));

        await ExecAsync("dbo.RolePermission_Remove",
            ("@RoleId", adminRoleId), ("@PermissionSlug", "roles.read"), ("@TenantId", tenantId));
    }

    /// <summary>
    /// Two administrators each removing the other at the same moment. Without the tenant row lock,
    /// both read the other as still present and both commit, leaving none.
    /// </summary>
    [SkippableFact]
    public async Task Concurrent_removals_cannot_both_succeed()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId, adminRoleId) = await CreateTenantAsync();
        int second = await AddMemberAsync(tenantId, adminRoleId);

        var results = await Task.WhenAll(
            AttemptAsync(() => ExecAsync("dbo.UserRole_Remove", ("@UserId", ownerId), ("@RoleId", adminRoleId), ("@TenantId", tenantId))),
            AttemptAsync(() => ExecAsync("dbo.UserRole_Remove", ("@UserId", second), ("@RoleId", adminRoleId), ("@TenantId", tenantId))));

        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal(1, results.Count(r => r is SqlException { Number: LastAdministrator }));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Tenant_Administrators(@p0)", tenantId));
    }

    // ---------------------------------------------------------------- helpers

    private static async Task AssertRejectedAsync(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(action);
        Assert.Equal(LastAdministrator, ex.Number);
    }

    private static async Task<Exception?> AttemptAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task<bool> IsAdministratorAsync(int tenantId, int userId) =>
        await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Tenant_Administrators(@p0) WHERE UserId = @p1", tenantId, userId) == 1;

    private async Task<(int TenantId, int OwnerId, int AdminRoleId)> CreateTenantAsync()
    {
        int ownerId = await CreateUserAsync();
        int tenantId = await CreateTenantForAsync(ownerId);
        int adminRoleId = await ScalarAsync<int>("SELECT Id FROM dbo.Roles WHERE TenantId = @p0 AND Name = 'Admin'", tenantId);
        return (tenantId, ownerId, adminRoleId);
    }

    /// <summary>True only when every access check agrees the user can work in the workspace.</summary>
    private async Task<bool> HasAccessAsync(int userId, int tenantId, int workspaceId)
    {
        bool verified = await ProcScalarAsync<int>("dbo.Permission_Verify",
            ("@UserId", userId), ("@TenantId", tenantId), ("@WorkspaceId", workspaceId), ("@PermissionSlug", "users.manage")) == 1;
        bool member = await ProcScalarAsync<bool>("dbo.WorkspaceMember_Verify", ("@UserId", userId), ("@WorkspaceId", workspaceId));
        bool effective = await ProcScalarAsync<string>("dbo.Permission_EffectiveSet", ("@UserId", userId), ("@WorkspaceId", workspaceId)) is not null;
        bool listed = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantMembers WHERE UserId = @p0 AND TenantId = @p1 AND IsSuspended = 0", userId, tenantId) == 1;

        Assert.True(verified == member && member == effective && effective == listed,
            $"access checks disagree: verify={verified}, member={member}, effective={effective}, listed={listed}");
        return verified;
    }

    private async Task<int> CreateTenantForAsync(int ownerId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Tenant_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Name", "guard-test");
        cmd.Parameters.AddWithValue("@Description", "guard-test");
        cmd.Parameters.AddWithValue("@OwnerUserId", ownerId);
        cmd.Parameters.AddWithValue("@WorkspaceName", "Default Workspace");
        cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        cmd.Parameters.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.Parameters["@TenantId"].Value;
    }

    private async Task<T?> ProcScalarAsync<T>(string procedure, params (string Name, object Value)[] parameters)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private async Task<int> AddMemberAsync(int tenantId, int? roleId)
    {
        int userId = await CreateUserAsync();
        await ExecSqlAsync("INSERT INTO dbo.TenantMembers (TenantId, UserId) VALUES (@p0, @p1);", tenantId, userId);
        if (roleId is not null)
        {
            await ExecAsync("dbo.UserRole_Assign", ("@UserId", userId), ("@RoleId", roleId.Value), ("@TenantId", tenantId));
        }

        return userId;
    }

    private async Task<int> CreateUserAsync()
    {
        string unique = $"guard-{Guid.NewGuid():N}";
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(
            """
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@Username, @Email, 'hash', 1);
            """, conn);
        cmd.Parameters.AddWithValue("@Username", unique);
        cmd.Parameters.AddWithValue("@Email", $"{unique}@example.test");
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task ExecAsync(string procedure, params (string Name, object Value)[] parameters)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ExecSqlAsync(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
