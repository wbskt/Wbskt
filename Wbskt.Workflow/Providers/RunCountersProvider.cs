using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class RunCountersProvider : BaseSqlProvider, IRunCountersProvider
{
    public RunCountersProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.RunCounters_GetBy_RunId",
            p => p.AddWithValue("@RunId", runId),
            Map,
            new KeyNotFoundException($"Run counters for RunId={runId} not found."),
            ct
        );
    }

    public async Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.RunCounters_IncrementActiveBranches",
            p =>
            {
                p.AddWithValue("@RunId", runId);
                p.AddWithValue("@Delta", delta);
            },
            r => r.GetInt32(0),
            new InvalidOperationException("RunCounters_IncrementActiveBranches did not return a value."),
            ct
        );
    }

    public async Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.RunCounters_DecrementActiveBranches",
            p =>
            {
                p.AddWithValue("@RunId", runId);
                p.AddWithValue("@Delta", delta);
            },
            r => r.GetInt32(0),
            new InvalidOperationException("RunCounters_DecrementActiveBranches did not return a value."),
            ct
        );
    }

    public async Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.RunCounters_AddCreditsConsumed",
            p =>
            {
                p.AddWithValue("@RunId", runId);
                p.AddWithValue("@Cost", cost);
            },
            r => r.GetDecimal(0),
            new InvalidOperationException("RunCounters_AddCreditsConsumed did not return a value."),
            ct
        );
    }

    public async Task<bool> TryChargeAsync(int runId, decimal cost, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.RunCounters_TryCharge",
            p =>
            {
                p.AddWithValue("@RunId", runId);
                p.AddWithValue("@Cost", cost);
            },
            ct
        );
        return result is decimal;
    }

    public async Task<long> SumActiveBranchesAsync(CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.RunCounters_SumActiveBranches",
            null,
            ct
        );
        return result is long count ? count : Convert.ToInt64(result ?? 0L);
    }

    internal static RunCountersRow Map(DbDataReader reader)
    {
        return new RunCountersRow
        {
            RunId = reader.GetInt32(reader.GetOrdinal("RunId")),
            ActiveBranchCount = reader.GetInt32(reader.GetOrdinal("ActiveBranchCount")),
            CreditsConsumed = reader.GetDecimal(reader.GetOrdinal("CreditsConsumed")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
        };
    }
}
