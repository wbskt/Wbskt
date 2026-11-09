using System.Data;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class RunRetentionProvider : BaseSqlProvider, IRunRetentionProvider
{
    public RunRetentionProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> DeleteRetiredRunsAsync(DateTime nowUtc, IReadOnlyCollection<RunHistoryRetention> retention, DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("PlanCode", typeof(string));
        table.Columns.Add("Days", typeof(int));
        foreach (RunHistoryRetention plan in retention)
        {
            table.Rows.Add(plan.PlanCode.ToString(), plan.Days);
        }

        return await ExecuteScalarAsync<int>("dbo.Run_DeleteRetired", p =>
        {
            p.Add("@NowUtc", SqlDbType.DateTime2).Value = nowUtc;
            var parameter = p.AddWithValue("@Retention", table);
            parameter.SqlDbType = SqlDbType.Structured;
            parameter.TypeName = "dbo.RunHistoryRetentionTableType";
            p.Add("@CutoffUtc", SqlDbType.DateTime2).Value = cutoffUtc;
            p.AddWithValue("@BatchSize", batchSize);
        }, ct);
    }
}
