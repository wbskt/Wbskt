using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;
using Wbskt.Common.Services;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedClientsReader : IClientsReader
{
    private readonly IClientsDatabaseReader _databaseReader;
    private readonly ICacheService _cacheService;

    public CachedClientsReader(IClientsDatabaseReader databaseReader, ICacheService cacheService)
    {
        _databaseReader = databaseReader;
        _cacheService = cacheService;
    }

    public async Task<ClientRecord?> GetByRefAsync(Guid refId, CancellationToken cancellationToken)
    {
        var clients = await GetAllClientsAsync(cancellationToken);
        return clients.FirstOrDefault(c => c.RefId == refId);
    }

    public async Task<List<ClientRecord>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken)
    {
        var clients = await GetAllClientsAsync(cancellationToken);
        return clients.Where(c => c.UserId == userId).ToList();
    }

    private Task<List<ClientRecord>> GetAllClientsAsync(CancellationToken cancellationToken)
    {
        return _cacheService.GetOrSetAsync("AllClients", () => _databaseReader.GetAllAsync(DateTime.UnixEpoch, cancellationToken), Constants.ExpiryTimes.CacheExpiry, cancellationToken);
    }
}
