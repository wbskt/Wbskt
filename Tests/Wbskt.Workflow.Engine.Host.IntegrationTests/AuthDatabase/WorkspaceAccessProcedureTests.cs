using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// Invitations that name workspaces, workspace ownership transfer, and the User role's default reads.
/// Every test builds its own tenant, so the shared database's other rows are never involved.
/// </summary>
[Collection("SqlEdge")]
public sealed class WorkspaceAccessProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    [SkippableFact]
    public async Task Accepting_an_invitation_joins_the_workspaces_it_names()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId) = await CreateTenantAsync();
        int named = await CreateWorkspaceAsync(tenantId, ownerId);
        int other = await CreateWorkspaceAsync(tenantId, ownerId);
        var (inviteeId, email) = await CreateUserWithEmailAsync();
        byte[] tokenHash = RandomHash();

        await CreateInvitationAsync(tenantId, email, tokenHash, ownerId, $"{named}");
        await AcceptAsync(tokenHash, inviteeId);

        Assert.True(await IsWorkspaceMemberAsync(named, inviteeId));
        Assert.False(await IsWorkspaceMemberAsync(other, inviteeId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantMembers WHERE TenantId = @p0 AND UserId = @p1", tenantId, inviteeId));
    }

    [SkippableFact]
    public async Task An_invitation_cannot_name_another_tenants_workspace()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId) = await CreateTenantAsync();
        var (otherTenantId, otherOwnerId) = await CreateTenantAsync();
        int foreign = await CreateWorkspaceAsync(otherTenantId, otherOwnerId);
        var (_, email) = await CreateUserWithEmailAsync();

        var ex = await Assert.ThrowsAsync<SqlException>(() => CreateInvitationAsync(tenantId, email, RandomHash(), ownerId, $"{foreign}"));

        Assert.Equal(50017, ex.Number);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantInvitations WHERE TenantId = @p0", tenantId));
    }

    [SkippableFact]
    public async Task A_workspace_deleted_before_acceptance_is_skipped()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId) = await CreateTenantAsync();
        int kept = await CreateWorkspaceAsync(tenantId, ownerId);
        int deleted = await CreateWorkspaceAsync(tenantId, ownerId);
        var (inviteeId, email) = await CreateUserWithEmailAsync();
        byte[] tokenHash = RandomHash();
        await CreateInvitationAsync(tenantId, email, tokenHash, ownerId, $"{kept},{deleted}");

        await ProcAsync("dbo.Workspace_Delete", ("@Id", deleted));
        await AcceptAsync(tokenHash, inviteeId);

        Assert.True(await IsWorkspaceMemberAsync(kept, inviteeId));
    }

    [SkippableFact]
    public async Task Ownership_moves_to_a_tenant_member_who_joins_the_workspace()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId) = await CreateTenantAsync();
        int workspaceId = await CreateWorkspaceAsync(tenantId, ownerId);
        int memberId = await CreateUserAsync();
        await ExecAsync("INSERT INTO dbo.TenantMembers (TenantId, UserId) VALUES (@p0, @p1)", tenantId, memberId);

        await ProcAsync("dbo.Workspace_SetOwner", ("@WorkspaceId", workspaceId), ("@UserId", memberId));

        Assert.Equal(memberId, await ScalarAsync<int>("SELECT OwnerUserId FROM dbo.Workspaces WHERE Id = @p0", workspaceId));
        Assert.True(await IsWorkspaceMemberAsync(workspaceId, memberId));

        // The previous owner is an ordinary member now, and can be removed.
        await ProcAsync("dbo.WorkspaceMember_Remove", ("@WorkspaceId", workspaceId), ("@UserId", ownerId));
        Assert.False(await IsWorkspaceMemberAsync(workspaceId, ownerId));
    }

    [SkippableFact]
    public async Task Ownership_cannot_move_outside_the_tenant()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId) = await CreateTenantAsync();
        int workspaceId = await CreateWorkspaceAsync(tenantId, ownerId);
        int outsiderId = await CreateUserAsync();

        var ex = await Assert.ThrowsAsync<SqlException>(() => ProcAsync("dbo.Workspace_SetOwner", ("@WorkspaceId", workspaceId), ("@UserId", outsiderId)));

        Assert.Equal(50009, ex.Number);
        Assert.Equal(ownerId, await ScalarAsync<int>("SELECT OwnerUserId FROM dbo.Workspaces WHERE Id = @p0", workspaceId));
        Assert.False(await IsWorkspaceMemberAsync(workspaceId, outsiderId));
    }

    [SkippableFact]
    public async Task A_new_tenants_User_role_can_read_a_workspaces_resources()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, _) = await CreateTenantAsync();

        string granted = await ScalarAsync<string>("""
            SELECT STRING_AGG(P.Slug, ',') WITHIN GROUP (ORDER BY P.Slug)
            FROM dbo.RolePermissions RP
            INNER JOIN dbo.Roles R ON R.Id = RP.RoleId
            INNER JOIN dbo.Permissions P ON P.Id = RP.PermissionId
            WHERE R.TenantId = @p0 AND R.Name = 'User' AND RP.IsDeny = 0
            """, tenantId);

        Assert.Equal("clients.read,logs.read,policies.read,templates.read,workflows.read", granted);
    }

    // ---------------------------------------------------------------- helpers

    private static byte[] RandomHash() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    private async Task<(int TenantId, int OwnerId)> CreateTenantAsync()
    {
        int ownerId = await CreateUserAsync();
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Tenant_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Name", "access-test");
        cmd.Parameters.AddWithValue("@Description", "access-test");
        cmd.Parameters.AddWithValue("@OwnerUserId", ownerId);
        cmd.Parameters.AddWithValue("@WorkspaceName", "Default Workspace");
        cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        cmd.Parameters.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return ((int)cmd.Parameters["@TenantId"].Value, ownerId);
    }

    private Task<int> CreateWorkspaceAsync(int tenantId, int ownerId) =>
        ScalarAsync<int>("""
            INSERT INTO dbo.Workspaces (RefId, TenantId, Name, OwnerUserId) OUTPUT INSERTED.Id VALUES (NEWID(), @p0, N'ws', @p1);
            """, tenantId, ownerId);

    private async Task<int> CreateUserAsync() => (await CreateUserWithEmailAsync()).Id;

    private async Task<(int Id, string Email)> CreateUserWithEmailAsync()
    {
        string name = $"access-{Guid.NewGuid():N}"[..27];
        string email = $"{name}@example.test";
        int id = await ScalarAsync<int>("""
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive) OUTPUT INSERTED.Id VALUES (@p0, @p1, 'hash', 1);
            """, name, email);
        return (id, email);
    }

    private async Task CreateInvitationAsync(int tenantId, string email, byte[] tokenHash, int invitedBy, string workspaceIds)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.TenantInvitation_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@Email", email);
        cmd.Parameters.AddWithValue("@RoleId", DBNull.Value);
        cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        cmd.Parameters.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(1));
        cmd.Parameters.AddWithValue("@InvitedByUserId", invitedBy);
        cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        cmd.Parameters.Add("@TenantName", SqlDbType.NVarChar, 100).Direction = ParameterDirection.Output;
        cmd.Parameters.AddWithValue("@WorkspaceIds", workspaceIds);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task AcceptAsync(byte[] tokenHash, int userId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.TenantInvitation_Accept", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<bool> IsWorkspaceMemberAsync(int workspaceId, int userId) =>
        await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkspaceMembers WHERE WorkspaceId = @p0 AND UserId = @p1", workspaceId, userId) == 1;

    private async Task ProcAsync(string procedure, params (string Name, object Value)[] parameters)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Command(sql, conn, args);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Command(sql, conn, args);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static SqlCommand Command(string sql, SqlConnection conn, object[] args)
    {
        var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return cmd;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
