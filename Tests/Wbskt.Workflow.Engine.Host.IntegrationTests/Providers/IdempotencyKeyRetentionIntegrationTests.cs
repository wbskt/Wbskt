using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// IdempotencyKey_DeleteExpired deletes the oldest expired keys a batch at a time and leaves the rest
/// (#1112). The rows here are dated 2001, before anything another test writes, so the sweep's cutoff
/// reaches only them.
/// </summary>
[Collection("SqlEdge")]
public sealed class IdempotencyKeyRetentionIntegrationTests(SqlEdgeFixture fixture)
{
    private static readonly DateTime Cutoff = new(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Expired_keys_go_oldest_first_a_batch_at_a_time_and_newer_ones_stay()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        string prefix = $"it-retention-{Guid.NewGuid():N}";
        await InsertKeyAsync($"{prefix}-c", new DateTime(2001, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        await InsertKeyAsync($"{prefix}-a", new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await InsertKeyAsync($"{prefix}-b", new DateTime(2001, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await InsertKeyAsync($"{prefix}-kept", new DateTime(2003, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var keys = ProviderFactory.IdempotencyKey(fixture.ConnectionString);

        int first = await keys.DeleteExpiredAsync(Cutoff, batchSize: 2, CancellationToken.None);
        IReadOnlyCollection<string> afterFirst = await RemainingAsync(prefix);
        int second = await keys.DeleteExpiredAsync(Cutoff, batchSize: 2, CancellationToken.None);
        int third = await keys.DeleteExpiredAsync(Cutoff, batchSize: 2, CancellationToken.None);

        first.Should().Be(2);
        afterFirst.Should().BeEquivalentTo([$"{prefix}-c", $"{prefix}-kept"]);
        second.Should().Be(1);
        third.Should().Be(0);
        (await RemainingAsync(prefix)).Should().BeEquivalentTo([$"{prefix}-kept"]);
    }

    private async Task InsertKeyAsync(string keyValue, DateTime createdAt)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            INSERT INTO dbo.IdempotencyKeys (KeyValue, RunId, BranchRefId, NodeId, Attempt, Status, CreatedAt)
            VALUES (@KeyValue, 0, NEWID(), NEWID(), 1, N'Succeeded', @CreatedAt);
            """, connection);
        command.Parameters.AddWithValue("@KeyValue", keyValue);
        command.Parameters.Add("@CreatedAt", System.Data.SqlDbType.DateTime2).Value = createdAt;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyCollection<string>> RemainingAsync(string prefix)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT KeyValue FROM dbo.IdempotencyKeys WHERE KeyValue LIKE @Prefix + N'%';", connection);
        command.Parameters.AddWithValue("@Prefix", prefix);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
