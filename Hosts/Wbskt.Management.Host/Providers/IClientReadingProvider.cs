using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

public interface IClientReadingProvider
{
    /// <summary>Stores the numbers from one state report. A reading already stored is skipped.</summary>
    Task InsertAsync(int clientId, DateTime deviceTime, DateTime receivedAt, bool isLate, IReadOnlyCollection<ClientReadingValue> readings, CancellationToken cancellationToken = default);

    /// <summary>One variable's readings in [from, to), summarised per bucket counted from <paramref name="fromUtc"/>.</summary>
    Task<IReadOnlyCollection<ClientReadingBucket>> GetBucketsAsync(int clientId, string name, DateTime fromUtc, DateTime toUtc, int bucketSeconds, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="maxRows"/> readings in [from, to), oldest first; every variable when <paramref name="name"/> is null.</summary>
    Task<IReadOnlyCollection<ClientReading>> GetRangeAsync(int clientId, string? name, DateTime fromUtc, DateTime toUtc, int maxRows, CancellationToken cancellationToken = default);

    /// <summary>Deletes up to <paramref name="batchSize"/> readings received before the cutoff; returns how many went.</summary>
    Task<int> DeleteBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default);
}
