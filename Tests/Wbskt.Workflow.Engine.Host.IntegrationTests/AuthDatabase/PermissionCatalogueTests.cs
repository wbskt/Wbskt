using System.Reflection;
using Microsoft.Data.SqlClient;
using Wbskt.Primitives.Constants;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// The permission catalogue (dbo.PermissionCatalogue, which the post-deployment script writes into
/// dbo.Permissions) is the one the hosts check against: a slug in <see cref="PermissionNames"/> with no
/// row could never be granted, and a row with no constant could be granted but never checked.
/// </summary>
[Collection("SqlEdge")]
public sealed class PermissionCatalogueTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    [SkippableFact]
    public async Task The_seeded_catalogue_is_exactly_the_permission_names()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        var expected = typeof(PermissionNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(expected, await ReadSlugsAsync(conn, "SELECT Slug FROM dbo.PermissionCatalogue"));
        Assert.Equal(expected, await ReadSlugsAsync(conn, "SELECT Slug FROM dbo.Permissions"));
    }

    private static async Task<List<string>> ReadSlugsAsync(SqlConnection conn, string sql)
    {
        var slugs = new List<string>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            slugs.Add(reader.GetString(0));
        }

        return slugs.Order(StringComparer.Ordinal).ToList();
    }
}
