using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AdminViews;

/// <summary>
/// The [admin] views are the platform admin host's (wbskt/Wbskt.Admin) only way into these databases,
/// so their columns are a contract with another repository. Each test pins one database's views, so
/// a rename or a dropped column fails here instead of in the admin host. Every view is also read as a
/// user granted nothing but SELECT on the [admin] schema, the way the post-deployment scripts grant
/// [wbskt_admin_reader]: the views must work through ownership chaining, and the tables must stay shut.
/// </summary>
[Collection("SqlEdge")]
public sealed class AdminViewContractTests(SqlEdgeFixture main, AuthSqlFixture auth)
{
    private const string Skipped = "SQL Server not reachable — skipping.";
    private const string Reader = "admin_view_contract_reader";

    private static readonly Dictionary<string, string[]> AuthViews = new()
    {
        ["People"] = ["Id", "RefId", "Username", "Email", "IsActive", "IsEmailVerified", "FailedLoginCount", "LockedUntil", "LastLoginAt", "PasswordChangedAt", "UsernameChangedAt", "UpdatedAt", "CreatedAt", "AccountCount", "WorkspaceCount"],
        ["Accounts"] = ["Id", "RefId", "Name", "Description", "PlanCode", "PlanSource", "PlanRevision", "PlanChangedAt", "PlanEndsAt", "PlanLimitOverrides", "CreatedAt", "MemberCount", "WorkspaceCount"],
        ["AccountMembers"] = ["TenantId", "UserId", "IsSuspended"],
        ["RoleAssignments"] = ["UserId", "TenantId", "WorkspaceId", "RoleRefId", "RoleName", "RoleKind"],
        ["Workspaces"] = ["Id", "RefId", "TenantId", "Name", "Description", "OwnerUserId", "CreatedAt", "DeletedAt", "MemberCount"],
        ["WorkspaceMembers"] = ["WorkspaceId", "UserId"],
        ["Sessions"] = ["SessionId", "UserId", "StartedAt", "LastRefreshedAt", "ExpiresAt", "RevokedAt", "IpAddress"],
        ["Subscriptions"] = ["TenantId", "Provider", "SubscriptionId", "CustomerId", "PlanCode", "BillingInterval", "Status", "CurrentPeriodEnd", "CancelAtPeriodEnd", "PastDueSince", "ScheduledPlanCode", "ScheduledBillingInterval", "ScheduledAt", "UpdatedAt", "CreatedAt"],
        ["CreditPurchases"] = ["TenantId", "Provider", "PaymentId", "Credits", "PurchasedAt", "ExpiresAt", "RefundedAt", "HeldAt", "CreatedAt"],
        ["StaffAudit"] = ["Id", "StaffRefId", "StaffName", "Action", "TargetKind", "TargetRefId", "Reason", "Outcome", "Detail", "IdempotencyKey", "CreatedAt"]
    };

    private static readonly Dictionary<string, string[]> MainViews = new()
    {
        ["WorkspaceAccounts"] = ["WorkspaceId", "TenantId", "TenantRefId", "PlanCode", "PlanRevision", "LimitOverrides", "CreditsPerMonth", "RawReadingsDays", "HourlyReadingsDays", "RunHistoryDays", "RetentionHeldUntil", "RetiredAt", "UpdatedAt", "CreatedAt", "MonthlyCreditCap"],
        ["WorkspaceCounts"] = ["WorkspaceId", "ClientCount", "ConnectedClientCount", "PolicyCount", "WorkflowCount"],
        ["MessageUsage"] = ["WorkspaceId", "Day", "Inbound", "Outbound"],
        ["CreditUsage"] = ["WorkspaceId", "Day", "Credits"],
        ["CreditMonths"] = ["TenantId", "MonthStart", "Posted", "Drawn"],
        ["CreditGrants"] = ["Id", "TenantId", "Credits", "Drawn", "GrantedAt", "ExpiresAt", "Source", "SourceRef", "RevokedAt", "HeldAt", "CreatedAt"],
        ["PersonEvents"] = ["Id", "UserId", "UserRefId", "EventName", "EventCriticality", "WorkspaceId", "Source", "ClientAddress", "UserAgent", "EventData", "MessageId", "CreatedAt"],
        ["StaffAudit"] = ["Id", "StaffRefId", "StaffName", "Action", "TargetKind", "TargetRefId", "Reason", "Outcome", "Detail", "IdempotencyKey", "CreatedAt"]
    };

    [SkippableFact]
    public async Task The_auth_database_admin_views_keep_their_columns_and_read_as_the_admin_reader()
    {
        Skip.IfNot(auth.IsAvailable, Skipped);

        await AssertViewsAsync(auth.ConnectionString, AuthViews, "dbo.Users");
    }

    [SkippableFact]
    public async Task The_main_database_admin_views_keep_their_columns_and_read_as_the_admin_reader()
    {
        Skip.IfNot(main.IsAvailable, Skipped);

        await AssertViewsAsync(main.ConnectionString, MainViews, "dbo.Clients");
    }

    [SkippableFact]
    public async Task Staff_audit_rows_cannot_be_changed_or_deleted_in_either_database()
    {
        Skip.IfNot(auth.IsAvailable && main.IsAvailable, Skipped);

        foreach (var connectionString in new[] { auth.ConnectionString, main.ConnectionString })
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await ExecuteAsync(conn, """
                INSERT INTO dbo.StaffAudit (StaffRefId, StaffName, Action, TargetKind, TargetRefId, Reason, Outcome, IdempotencyKey)
                VALUES (NEWID(), N'pika', N'person.lock', N'Person', NEWID(), N'contract test', N'Succeeded', NEWID());
                """);

            var update = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(conn, "UPDATE dbo.StaffAudit SET Reason = N'changed';"));
            var delete = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(conn, "DELETE FROM dbo.StaffAudit;"));

            Assert.Equal(50090, update.Number);
            Assert.Equal(50090, delete.Number);
            Assert.Equal(0, await ScalarAsync(conn, "SELECT COUNT(*) FROM dbo.StaffAudit WHERE Reason <> N'contract test';"));
        }
    }

    private static async Task AssertViewsAsync(string connectionString, Dictionary<string, string[]> expected, string closedTable)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var actual = await ReadViewColumnsAsync(conn);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (view, columns) in expected)
        {
            Assert.Equal(columns, actual[view]);
        }

        await ExecuteAsync(conn, $"""
            IF DATABASE_PRINCIPAL_ID(N'{Reader}') IS NULL
                CREATE USER [{Reader}] WITHOUT LOGIN;
            GRANT SELECT ON SCHEMA::[admin] TO [{Reader}];
            """);

        await ExecuteAsync(conn, $"EXECUTE AS USER = N'{Reader}';");
        try
        {
            foreach (var view in expected.Keys)
            {
                await ExecuteAsync(conn, $"SELECT TOP (1) * FROM [admin].[{view}];");
            }

            var refused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(conn, $"SELECT TOP (1) * FROM {closedTable};"));
            Assert.Equal(229, refused.Number);
        }
        finally
        {
            await ExecuteAsync(conn, "REVERT;");
        }
    }

    private static async Task<Dictionary<string, string[]>> ReadViewColumnsAsync(SqlConnection conn)
    {
        await using var cmd = new SqlCommand("""
            SELECT v.name, c.name
            FROM sys.views v
            INNER JOIN sys.columns c ON c.object_id = v.object_id
            WHERE v.schema_id = SCHEMA_ID(N'admin')
            ORDER BY v.name, c.column_id;
            """, conn);
        var columns = new Dictionary<string, List<string>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var view = reader.GetString(0);
            if (!columns.TryGetValue(view, out var list))
            {
                list = [];
                columns[view] = list;
            }

            list.Add(reader.GetString(1));
        }

        return columns.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static async Task ExecuteAsync(SqlConnection conn, string sql)
    {
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(SqlConnection conn, string sql)
    {
        await using var cmd = new SqlCommand(sql, conn);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
