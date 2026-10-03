using System.Data;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class RunRetentionProvider : BaseSqlProvider, IRunRetentionProvider
{
    public RunRetentionProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> DeleteRetiredRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        return await ExecuteScalarAsync<int>("dbo.Run_DeleteRetired", p =>
        {
            p.Add("@CutoffUtc", SqlDbType.DateTime2).Value = cutoffUtc;
            p.AddWithValue("@BatchSize", batchSize);
        }, ct);
    }
}
