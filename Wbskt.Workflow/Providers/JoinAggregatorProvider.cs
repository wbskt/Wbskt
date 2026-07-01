using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class JoinAggregatorProvider : BaseSqlProvider, IJoinAggregatorProvider
{
    public JoinAggregatorProvider(IConfiguration configuration) : base(configuration) { }

    public async Task InitializeAsync(Guid joinToken, int runId, int expectedCount, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.JoinAggregator_Initialize",
            p =>
            {
                p.AddWithValue("@JoinToken", joinToken);
                p.AddWithValue("@RunId", runId);
                p.AddWithValue("@ExpectedCount", expectedCount);
            },
            ct
        );
    }

    public async Task<JoinContributionResult> ContributeAsync(Guid joinToken, string outcome, string mode, int quorumCount, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.JoinAggregator_Contribute",
            p =>
            {
                p.AddWithValue("@JoinToken", joinToken);
                p.AddWithValue("@Outcome", outcome);
                p.AddWithValue("@Mode", mode);
                p.AddWithValue("@QuorumCount", quorumCount);
            },
            Map,
            new InvalidOperationException("JoinAggregator_Contribute did not return a row."),
            ct
        );
    }

    internal static JoinContributionResult Map(DbDataReader reader)
    {
        return new JoinContributionResult(
            ShouldContinue: reader.GetBoolean(reader.GetOrdinal("ShouldContinue")),
            ContributedCount: reader.GetInt32(reader.GetOrdinal("ContributedCount")),
            SucceededCount: reader.GetInt32(reader.GetOrdinal("SucceededCount")),
            FailedCount: reader.GetInt32(reader.GetOrdinal("FailedCount")),
            ExpectedCount: reader.GetInt32(reader.GetOrdinal("ExpectedCount"))
        );
    }
}
