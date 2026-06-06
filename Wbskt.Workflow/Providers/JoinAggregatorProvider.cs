using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

public sealed class JoinAggregatorProvider : BaseSqlProvider, IJoinAggregatorProvider
{
    private readonly string _connectionString;

    public JoinAggregatorProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task InitializeAsync(Guid joinToken, int runId, int expectedCount, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.JoinAggregator_Initialize", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@JoinToken", joinToken);
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@ExpectedCount", expectedCount);

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<JoinContributionResult> ContributeAsync(Guid joinToken, string outcome, string mode, int quorumCount, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.JoinAggregator_Contribute", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@JoinToken", joinToken);
        command.Parameters.AddWithValue("@Outcome", outcome);
        command.Parameters.AddWithValue("@Mode", mode);
        command.Parameters.AddWithValue("@QuorumCount", quorumCount);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("JoinAggregator_Contribute did not return a row.");
    }

    internal static JoinContributionResult Map(DbDataReader reader)
    {
        return new JoinContributionResult(
            ShouldContinue: reader.GetBoolean(reader.GetOrdinal("ShouldContinue")),
            ContributedCount: reader.GetInt32(reader.GetOrdinal("ContributedCount")),
            SucceededCount: reader.GetInt32(reader.GetOrdinal("SucceededCount")),
            FailedCount: reader.GetInt32(reader.GetOrdinal("FailedCount")),
            ExpectedCount: reader.GetInt32(reader.GetOrdinal("ExpectedCount")));
    }
}
