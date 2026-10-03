using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class JoinAggregatorProvider : BaseSqlProvider, IJoinAggregatorProvider
{
    public JoinAggregatorProvider(IConfiguration configuration) : base(configuration) { }

    public async Task InitializeAsync(
        Guid joinToken,
        int runId,
        int expectedCount,
        string mode,
        int quorumCount,
        Guid? joinNodeId,
        CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.JoinAggregator_Initialize",
            p =>
            {
                p.AddWithValue("@JoinToken", joinToken);
                p.AddWithValue("@RunId", runId);
                p.AddWithValue("@ExpectedCount", expectedCount);
                p.AddWithValue("@Mode", mode);
                p.AddWithValue("@QuorumCount", quorumCount);
                p.AddWithValue("@JoinNodeId", (object?)joinNodeId ?? DBNull.Value);
            },
            ct
        );
    }

    public async Task<JoinContributionResult> ContributeAsync(Guid joinToken, long branchId, string outcome, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.JoinAggregator_Contribute",
            p =>
            {
                p.AddWithValue("@JoinToken", joinToken);
                p.AddWithValue("@BranchId", branchId);
                p.AddWithValue("@Outcome", outcome);
            },
            Map,
            new InvalidOperationException("JoinAggregator_Contribute did not return a row."),
            ct
        );
    }

    public async Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.JoinAggregator_DeleteAllBy_RunId",
            p => p.AddWithValue("@RunId", runId),
            ct
        );
    }

    internal static JoinContributionResult Map(DbDataReader reader)
    {
        int joinNodeIdOrdinal = reader.GetOrdinal("JoinNodeId");

        return new JoinContributionResult(
            ShouldContinue: reader.GetBoolean(reader.GetOrdinal("ShouldContinue")),
            ContributedCount: reader.GetInt32(reader.GetOrdinal("ContributedCount")),
            SucceededCount: reader.GetInt32(reader.GetOrdinal("SucceededCount")),
            FailedCount: reader.GetInt32(reader.GetOrdinal("FailedCount")),
            ExpectedCount: reader.GetInt32(reader.GetOrdinal("ExpectedCount")),
            JoinNodeId: reader.IsDBNull(joinNodeIdOrdinal) ? null : reader.GetGuid(joinNodeIdOrdinal)
        );
    }
}
