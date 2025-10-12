using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

/// <summary>
/// A cached implementation of the client reader that uses a ConcurrentDictionary and SqlDependency.
/// </summary>
internal sealed class CachedClientsReader : IClientsReader
{
    private readonly ILogger<CachedClientsReader> _logger;
    private readonly IClientsDatabaseReader _databaseReader;

    private static DateTime _lastModified = DateTime.UnixEpoch;
    private static readonly ConcurrentDictionary<Guid, ClientRecord> ClientsByRefCache = [];
    private static readonly ConcurrentDictionary<int, ClientRecord> ClientsByIdCache = [];

    public CachedClientsReader(ILogger<CachedClientsReader> logger, IClientsDatabaseReader databaseReader)
    {
        _logger = logger;
        _databaseReader = databaseReader;
    }

    public async Task<ClientRecord?> GetByRefAsync(Guid refId, CancellationToken cancellationToken)
    {
        await RefreshCacheIfEmpty(cancellationToken);
        ClientsByRefCache.TryGetValue(refId, out var client);
        return client;
    }

    public async Task<List<ClientRecord>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken)
    {
        await RefreshCacheIfEmpty(cancellationToken);
        return ClientsByIdCache.Values.Where(c => c.UserId == userId).ToList();
    }

    private async Task RefreshCacheIfEmpty(CancellationToken cancellationToken)
    {
        if (ClientsByIdCache.IsEmpty)
        {
            await RefreshCache(cancellationToken);
        }
    }

    private async Task RefreshCache(CancellationToken cancellationToken)
    {
        try
        {
            var clients = await _databaseReader.GetAllAsync(_lastModified, cancellationToken);
            var maxLastModified = _lastModified;

            foreach (var client in clients)
            {
                ClientsByIdCache[client.Id] = client;
                ClientsByRefCache[client.RefId] = client;

                if (client.LastModified > maxLastModified)
                {
                    maxLastModified = client.LastModified;
                }
            }

            _lastModified = maxLastModified;
            _logger.LogDebug("Clients cache refreshed with {count} clients", clients.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh clients cache");
            throw;
        }
    }
}
