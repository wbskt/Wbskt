using System.Collections.Concurrent;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedServersReader : IServersReader
{
    private readonly IServersDatabaseReader _databaseReader;
    private static readonly ConcurrentDictionary<int, ServerRecord> ServersByIdCache = new();
    private static DateTime _lastModified = DateTime.UnixEpoch;

    public CachedServersReader(IServersDatabaseReader databaseReader)
    {
        _databaseReader = databaseReader;
    }

    public async Task<List<ServerRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        await RefreshCacheAsync(cancellationToken);
        return ServersByIdCache.Values.ToList();
    }

    private async Task RefreshCacheAsync(CancellationToken cancellationToken)
    {
        var servers = await _databaseReader.GetAllAsync(_lastModified, cancellationToken);

        foreach (var server in servers)
        {
            ServersByIdCache[server.Id] = server;

            if (server.LastModified > _lastModified)
            {
                _lastModified = server.LastModified;
            }
        }
    }
}
