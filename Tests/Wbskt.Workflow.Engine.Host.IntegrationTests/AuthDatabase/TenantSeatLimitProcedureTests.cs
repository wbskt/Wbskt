using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// The plan's limits on a tenant's workspaces (dbo.Workspace_Create, 50030) and members
/// (dbo.TenantInvitation_Create and dbo.TenantInvitation_Accept, 50031), counted under a lock on the
/// tenant's row.
/// </summary>
[Collection("SqlEdge")]
public sealed class TenantSeatLimitProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";
    private const int WorkspaceLimitError = 50030;
    private const int MemberLimitError = 50031;

    [SkippableFact]
    public async Task A_workspace_past_the_limit_is_refused_and_another_tenant_is_not_counted()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        int other = await CreateTenantAsync();
        await CreateWorkspaceAsync(other, limit: null);
        await CreateWorkspaceAsync(tenantId, limit: 2);
        await CreateWorkspaceAsync(tenantId, limit: 2);

        var refused = await Assert.ThrowsAsync<SqlException>(() => CreateWorkspaceAsync(tenantId, limit: 2));

        Assert.Equal(WorkspaceLimitError, refused.Number);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Workspaces WHERE TenantId = @p0", tenantId));
        await CreateWorkspaceAsync(tenantId, limit: null);
    }

    [SkippableFact]
    public async Task Concurrent_workspaces_cannot_overshoot_the_limit()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        int[] owners = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CreateUserAsync()));

        Task[] attempts = owners.Select(owner => Task.Run(() => CreateWorkspaceAsync(tenantId, limit: 3, owner))).ToArray();
        try
        {
            await Task.WhenAll(attempts);
        }
        catch (SqlException)
        {
            // The attempts past the limit; each is checked below.
        }

        Assert.All(attempts.Where(t => t.IsFaulted), t => Assert.Equal(WorkspaceLimitError, Assert.IsType<SqlException>(t.Exception!.InnerException).Number));
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Workspaces WHERE TenantId = @p0", tenantId));
    }

    [SkippableFact]
    public async Task An_invitation_to_a_full_tenant_is_refused()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        int owner = await JoinAsync(tenantId);

        var refused = await Assert.ThrowsAsync<SqlException>(() => InviteAsync(tenantId, "someone@example.test", RandomNumberGenerator.GetBytes(32), owner, memberLimit: 1));

        Assert.Equal(MemberLimitError, refused.Number);
        await InviteAsync(tenantId, "someone@example.test", RandomNumberGenerator.GetBytes(32), owner, memberLimit: 2);
    }

    [SkippableFact]
    public async Task Accepting_past_the_limit_is_refused_and_leaves_the_invitation_unspent()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        int owner = await JoinAsync(tenantId);
        (int firstId, string firstEmail) = await CreateUserWithEmailAsync();
        (int secondId, string secondEmail) = await CreateUserWithEmailAsync();
        byte[] first = RandomNumberGenerator.GetBytes(32);
        byte[] second = RandomNumberGenerator.GetBytes(32);
        await InviteAsync(tenantId, firstEmail, first, owner, memberLimit: 2);
        await InviteAsync(tenantId, secondEmail, second, owner, memberLimit: 2);

        await AcceptAsync(first, firstId, memberLimit: 2);
        var refused = await Assert.ThrowsAsync<SqlException>(() => AcceptAsync(second, secondId, memberLimit: 2));

        Assert.Equal(MemberLimitError, refused.Number);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantMembers WHERE TenantId = @p0", tenantId));
        Assert.Null(await ScalarAsync<DateTime?>("SELECT AcceptedAt FROM dbo.TenantInvitations WHERE TokenHash = @p0", second));
        await AcceptAsync(second, secondId, memberLimit: 3);
    }

    private async Task<int> CreateTenantAsync() =>
        await ScalarAsync<int>("INSERT INTO dbo.Tenants (RefId, Name) OUTPUT INSERTED.Id VALUES (NEWID(), @p0);", $"seat-{Guid.NewGuid():N}"[..30]);

    private async Task<int> JoinAsync(int tenantId)
    {
        int userId = await CreateUserAsync();
        await ScalarAsync<int>("INSERT INTO dbo.TenantMembers (TenantId, UserId) VALUES (@p0, @p1); SELECT 1;", tenantId, userId);
        return userId;
    }

    private async Task CreateWorkspaceAsync(int tenantId, int? limit, int? ownerId = null)
    {
        int owner = ownerId ?? await CreateUserAsync();
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Name", $"seat-{Guid.NewGuid():N}"[..30]);
        cmd.Parameters.AddWithValue("@Description", "seat-test");
        cmd.Parameters.AddWithValue("@OwnerUserId", owner);
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        cmd.Parameters.AddWithValue("@WorkspaceLimit", (object?)limit ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InviteAsync(int tenantId, string email, byte[] tokenHash, int invitedBy, int? memberLimit)
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
        cmd.Parameters.AddWithValue("@MemberLimit", (object?)memberLimit ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task AcceptAsync(byte[] tokenHash, int userId, int? memberLimit)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.TenantInvitation_Accept", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        cmd.Parameters.AddWithValue("@MemberLimit", (object?)memberLimit ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CreateUserAsync() => (await CreateUserWithEmailAsync()).Id;

    private async Task<(int Id, string Email)> CreateUserWithEmailAsync()
    {
        string name = $"seat-{Guid.NewGuid():N}"[..27];
        string email = $"{name}@example.test";
        int id = await ScalarAsync<int>("INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive) OUTPUT INSERTED.Id VALUES (@p0, @p1, 'hash', 1);", name, email);
        return (id, email);
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        object? value = await cmd.ExecuteScalarAsync();
        return value is DBNull or null ? default! : (T)value;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
