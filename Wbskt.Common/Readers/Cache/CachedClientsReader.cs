using Microsoft.Extensions.Caching.Memory;
using Wbskt.Common.Records;
using Wbskt.Common.Readers.Database;

namespace Wbskt.Common.Readers.Cache;

/// <summary>
/// A cached implementation of the client reader to reduce database load.
/// </summary>
internal sealed class CachedClientsReader : IClientsReader
{
    private readonly IClientsDatabaseReader _databaseReader;
    private readonly IMemoryCache _cache;
    private static readonly string CacheKey = "Clients_All";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public CachedClientsReader(IClientsDatabaseReader databaseReader, IMemoryCache cache)
    {
        _databaseReader = databaseReader;
        _cache = cache;
    }

    public async Task<ClientRecord?> GetByRefAsync(Guid refId, CancellationToken cancellationToken)
    {
        var clients = await GetAllFromCacheAsync(cancellationToken);
        return clients.FirstOrDefault(c => c.RefId == refId);
    }

    public async Task<List<ClientRecord>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken)
    {
        var clients = await GetAllFromCacheAsync(cancellationToken);
        return clients.Where(c => c.UserId == userId).ToList();
    }

    private async Task<List<ClientRecord>> GetAllFromCacheAsync(CancellationToken cancellationToken)
    {
        return await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _databaseReader.GetAllAsync(cancellationToken);
        }) ?? [];
    }
}
