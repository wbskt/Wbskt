using Wbskt.Models;
using System.Data;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class ClientReadingProvider : BaseSqlProvider, IClientReadingProvider
{
    public ClientReadingProvider(IConfiguration configuration) : base(configuration) { }

    public async Task InsertAsync(int clientId, DateTime sentAt, DateTime receivedAt, bool isLate, IReadOnlyCollection<ClientReadingValue> readings, CancellationToken cancellationToken = default)
    {
        using var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("NumberValue", typeof(double));
        foreach (var reading in readings)
        {
            table.Rows.Add(reading.Name, reading.Value);
        }

        await ExecuteNonQueryAsync("dbo.ClientReadings_InsertBatch", p =>
        {
            p.AddWithValue("@ClientId", clientId);
            p.Add("@SentAt", SqlDbType.DateTime2).Value = sentAt;
            p.Add("@ReceivedAt", SqlDbType.DateTime2).Value = receivedAt;
            p.AddWithValue("@IsLate", isLate);
            var parameter = p.AddWithValue("@Readings", table);
            parameter.SqlDbType = SqlDbType.Structured;
            parameter.TypeName = "dbo.ClientReadingTableType";
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ClientReadingBucket>> GetBucketsAsync(int clientId, string name, DateTime fromUtc, DateTime toUtc, int bucketSeconds, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync("dbo.ClientReadings_GetBuckets", p =>
            {
                p.AddWithValue("@ClientId", clientId);
                p.AddWithValue("@Name", name);
                p.Add("@FromUtc", SqlDbType.DateTime2).Value = fromUtc;
                p.Add("@ToUtc", SqlDbType.DateTime2).Value = toUtc;
                p.AddWithValue("@BucketSeconds", bucketSeconds);
            },
            reader => new ClientReadingBucket(
                reader.GetDateTime(reader.GetOrdinal("BucketStart")).ToUtcOffset(),
                reader.GetInt64(reader.GetOrdinal("ReadingCount")),
                reader.GetDouble(reader.GetOrdinal("MinValue")),
                reader.GetDouble(reader.GetOrdinal("AvgValue")),
                reader.GetDouble(reader.GetOrdinal("MaxValue"))),
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<ClientReading>> GetRangeAsync(int clientId, string? name, DateTime fromUtc, DateTime toUtc, int maxRows, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync("dbo.ClientReadings_GetRange", p =>
            {
                p.AddWithValue("@ClientId", clientId);
                p.AddWithValue("@Name", (object?)name ?? DBNull.Value);
                p.Add("@FromUtc", SqlDbType.DateTime2).Value = fromUtc;
                p.Add("@ToUtc", SqlDbType.DateTime2).Value = toUtc;
                p.AddWithValue("@MaxRows", maxRows);
            },
            reader => new ClientReading(
                reader.GetString(reader.GetOrdinal("Name")),
                DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("SentAt")), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("ReceivedAt")), DateTimeKind.Utc),
                reader.GetDouble(reader.GetOrdinal("NumberValue")),
                reader.GetBoolean(reader.GetOrdinal("IsLate"))),
            cancellationToken);
    }

    public Task<ReadingCompaction> CompactAsync(DateTime nowUtc, IReadOnlyCollection<ReadingRetention> retention, int batchSize, CancellationToken cancellationToken = default)
    {
        var table = new DataTable();
        table.Columns.Add("PlanCode", typeof(string));
        table.Columns.Add("RawDays", typeof(int));
        table.Columns.Add("HourlyDays", typeof(int));
        foreach (ReadingRetention plan in retention)
        {
            table.Rows.Add(plan.PlanCode.ToString(), plan.RawDays, plan.HourlyDays);
        }

        return ExecuteSingleAsync("dbo.ClientReadings_Compact", p =>
        {
            p.Add("@Now", SqlDbType.DateTime2).Value = nowUtc;
            var parameter = p.AddWithValue("@Retention", table);
            parameter.SqlDbType = SqlDbType.Structured;
            parameter.TypeName = "dbo.ReadingRetentionTableType";
            p.AddWithValue("@BatchSize", batchSize);
        }, r => new ReadingCompaction(
            r.GetInt32(r.GetOrdinal("RolledUp")),
            r.GetInt32(r.GetOrdinal("HourlyDeleted")),
            r.GetInt32(r.GetOrdinal("Cleared"))),
            new InvalidOperationException("ClientReadings_Compact did not return a row."),
            cancellationToken);
    }
}
