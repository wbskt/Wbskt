using System.Data;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class CreditUsageProvider : BaseSqlProvider, ICreditUsageProvider
{
    public CreditUsageProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> PostAsync(DateOnly day, DateTime endedSinceUtc, int batchSize, CancellationToken ct)
    {
        return await ExecuteScalarAsync<int>("dbo.RunCredits_Post", p =>
        {
            p.Add("@Day", SqlDbType.Date).Value = day.ToDateTime(TimeOnly.MinValue);
            p.Add("@EndedSince", SqlDbType.DateTime2).Value = endedSinceUtc;
            p.AddWithValue("@BatchSize", batchSize);
        }, ct);
    }
}
