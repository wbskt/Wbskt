using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.StaffActions;

/// <summary>
/// A staff suspension or removal against a real auth database, through the console's procedures and the
/// staff runner: the change commits with its audit row, a guard that refuses it leaves the tenant as it
/// was with only a refused row, and a retry with the same key replays rather than finding nothing to do.
/// </summary>
[Collection("SqlEdge")]
public sealed class AdminMemberSqlTests(AuthSqlFixture auth)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    private readonly CountingBus _bus = new();

    private AdminMemberService Service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AuthDBConnection"] = auth.ConnectionString })
            .Build();
        var runner = new StaffActionRunner(new StaffAuditProvider(configuration, "AuthDBConnection"), NullLogger<StaffActionRunner>.Instance);
        return new AdminMemberService(
            new StaffUserProvider(configuration),
            new TenantProvider(configuration),
            new AccountProvider(configuration),
            _bus,
            new AuditWorkspaces(new WorkspaceProvider(configuration), NullLogger<AuditWorkspaces>.Instance),
            runner,
            NullLogger<AdminMemberService>.Instance);
    }

    private static StaffChange Staff(Guid key) => new(Guid.NewGuid(), "pika", "Integration test of a member change", key);

    [SkippableFact]
    public async Task A_suspension_commits_with_its_audit_row_and_a_retry_replays()
    {
        Skip.IfNot(auth.IsAvailable, Skipped);
        var (tenantRef, tenantId, _, adminRoleId) = await CreateTenantAsync();
        var (memberId, memberRef) = await AddMemberAsync(tenantId, adminRoleId);
        var key = Guid.NewGuid();
        var request = new AdminSetMemberSuspendedRequest(Staff(key), true);

        var first = await Service().SetSuspendedAsync(tenantRef, memberRef, request, CancellationToken.None);
        var retry = await Service().SetSuspendedAsync(tenantRef, memberRef, request, CancellationToken.None);

        Assert.False(first.Value.Replayed);
        Assert.True(retry.Value.Replayed);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantMembers WHERE TenantId = @p0 AND UserId = @p1 AND IsSuspended = 1", tenantId, memberId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @p0 AND Outcome = N'Succeeded' AND Action = N'SuspendMember' AND TargetRefId = @p1 AND Detail LIKE N'Member %'", key, tenantRef));
        Assert.Equal(3, _bus.Published);
    }

    [SkippableFact]
    public async Task Suspending_the_last_administrator_leaves_them_and_only_a_refused_row()
    {
        Skip.IfNot(auth.IsAvailable, Skipped);
        var (tenantRef, tenantId, ownerId, _) = await CreateTenantAsync();
        var ownerRef = await ScalarAsync<Guid>("SELECT RefId FROM dbo.Users WHERE Id = @p0", ownerId);
        var key = Guid.NewGuid();

        var result = await Service().SetSuspendedAsync(tenantRef, ownerRef, new AdminSetMemberSuspendedRequest(Staff(key), true), CancellationToken.None);

        Assert.Equal("TENANT_LAST_ADMINISTRATOR", result.Error.Code);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantMembers WHERE TenantId = @p0 AND UserId = @p1 AND IsSuspended = 1", tenantId, ownerId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @p0 AND Outcome = N'Refused' AND Detail = N'TENANT_LAST_ADMINISTRATOR'", key));
        Assert.Equal(0, _bus.Published);
    }

    [SkippableFact]
    public async Task A_removal_passes_their_workspaces_to_an_administrator_and_a_retry_replays()
    {
        Skip.IfNot(auth.IsAvailable, Skipped);
        var (tenantRef, tenantId, ownerId, _) = await CreateTenantAsync();
        var (memberId, memberRef) = await AddMemberAsync(tenantId, null);
        await CreateTenantForAsync(memberId);
        await ExecuteAsync("UPDATE dbo.Workspaces SET OwnerUserId = @p1 WHERE TenantId = @p0", tenantId, memberId);
        var key = Guid.NewGuid();
        var request = new AdminRemoveMemberRequest(Staff(key));

        var first = await Service().RemoveAsync(tenantRef, memberRef, request, CancellationToken.None);
        var retry = await Service().RemoveAsync(tenantRef, memberRef, request, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(retry.Value.Replayed);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantMembers WHERE TenantId = @p0 AND UserId = @p1", tenantId, memberId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Workspaces WHERE TenantId = @p0 AND OwnerUserId <> @p1", tenantId, ownerId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @p0 AND Outcome = N'Succeeded' AND Action = N'RemoveMember'", key));
    }

    [SkippableFact]
    public async Task Removing_a_person_from_their_only_tenant_is_refused_and_leaves_them()
    {
        Skip.IfNot(auth.IsAvailable, Skipped);
        var (tenantRef, tenantId, _, _) = await CreateTenantAsync();
        var (memberId, memberRef) = await AddMemberAsync(tenantId, null);
        var key = Guid.NewGuid();

        var result = await Service().RemoveAsync(tenantRef, memberRef, new AdminRemoveMemberRequest(Staff(key)), CancellationToken.None);

        Assert.Equal("MEMBER_ONLY_TENANT", result.Error.Code);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantMembers WHERE TenantId = @p0 AND UserId = @p1", tenantId, memberId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @p0 AND Outcome = N'Refused' AND Detail = N'MEMBER_ONLY_TENANT'", key));
    }

    [SkippableFact]
    public async Task Removing_the_only_administrator_is_refused()
    {
        Skip.IfNot(auth.IsAvailable, Skipped);
        var (tenantRef, tenantId, ownerId, _) = await CreateTenantAsync();
        await AddMemberAsync(tenantId, null);
        await CreateTenantForAsync(ownerId);
        var ownerRef = await ScalarAsync<Guid>("SELECT RefId FROM dbo.Users WHERE Id = @p0", ownerId);

        var result = await Service().RemoveAsync(tenantRef, ownerRef, new AdminRemoveMemberRequest(Staff(Guid.NewGuid())), CancellationToken.None);

        Assert.Equal("TENANT_LAST_ADMINISTRATOR", result.Error.Code);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantMembers WHERE TenantId = @p0 AND UserId = @p1", tenantId, ownerId));
    }

    private async Task<(Guid TenantRef, int TenantId, int OwnerId, int AdminRoleId)> CreateTenantAsync()
    {
        int ownerId = await CreateUserAsync();
        int tenantId = await CreateTenantForAsync(ownerId);
        var tenantRef = await ScalarAsync<Guid>("SELECT RefId FROM dbo.Tenants WHERE Id = @p0", tenantId);
        int adminRoleId = await ScalarAsync<int>("SELECT Id FROM dbo.Roles WHERE TenantId = @p0 AND Name = 'Admin'", tenantId);
        return (tenantRef, tenantId, ownerId, adminRoleId);
    }

    private async Task<int> CreateTenantForAsync(int ownerId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Tenant_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Name", "staff-member-test");
        cmd.Parameters.AddWithValue("@Description", "staff-member-test");
        cmd.Parameters.AddWithValue("@OwnerUserId", ownerId);
        cmd.Parameters.AddWithValue("@WorkspaceName", "Default Workspace");
        cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        cmd.Parameters.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.Parameters["@TenantId"].Value;
    }

    private async Task<(int Id, Guid RefId)> AddMemberAsync(int tenantId, int? roleId)
    {
        int userId = await CreateUserAsync();
        await ExecuteAsync("INSERT INTO dbo.TenantMembers (TenantId, UserId) VALUES (@p0, @p1);", tenantId, userId);
        if (roleId is not null)
        {
            await ExecuteAsync("EXEC dbo.UserRole_Assign @UserId = @p0, @RoleId = @p1, @TenantId = @p2;", userId, roleId.Value, tenantId);
        }

        return (userId, await ScalarAsync<Guid>("SELECT RefId FROM dbo.Users WHERE Id = @p0", userId));
    }

    private async Task<int> CreateUserAsync()
    {
        string unique = $"staff-{Guid.NewGuid():N}"[..30];
        return await ScalarAsync<int>(
            "INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive) OUTPUT INSERTED.Id VALUES (@p0, @p1, 'hash', 1);",
            unique, $"{unique}@example.test");
    }

    private async Task ExecuteAsync(string sql, params object[] args)
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
        var conn = new SqlConnection(auth.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private sealed class CountingBus : IEventBus
    {
        public int Published { get; private set; }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            Published++;
            return Task.CompletedTask;
        }
    }
}
