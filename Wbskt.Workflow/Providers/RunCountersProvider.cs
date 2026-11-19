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
    private readonly TimeProvider _clock;

    public RunCountersProvider(IConfiguration configuration, TimeProvider? clock = null) : base(configuration)
    {
        _clock = clock ?? TimeProvider.System;
    }

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

    public async Task<CreditCharge?> TryChargeAsync(int runId, decimal cost, CancellationToken ct)
    {
        SqlParameter? refusal = null;
        CreditCharge? charge = await ExecuteFindAsync(
            "dbo.RunCounters_TryCharge",
            p =>
            {
                p.AddWithValue("@RunId", runId);
                p.AddWithValue("@Cost", cost);
                p.Add("@NowUtc", SqlDbType.DateTime2).Value = _clock.GetUtcNow().UtcDateTime;
                refusal = p.Add("@Refusal", SqlDbType.NVarChar, 20);
                refusal.Direction = ParameterDirection.Output;
            },
            r => new CreditCharge(r.GetDecimal(0), r.GetDecimal(1)),
            ct
        );

        // The output is set once the reader is closed, which ExecuteFindAsync has done by now.
        if (charge is not null)
        {
            return charge;
        }

        return CreditCharge.Refused(refusal?.Value is string reason && Enum.TryParse(reason, out CreditRefusal parsed) ? parsed : CreditRefusal.RunBudget);
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
