using System.Collections.Concurrent;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedClientsReader(ILogger<CachedClientsReader> logger, IClientsDatabaseReader clientsReader) : IDatabaseChangeListener, IClientsReader
{
    private static DateTime _lastModified = DateTime.MinValue;
    private readonly ConcurrentDictionary<int, ClientReadRecord> clientsCache = [];
    private readonly ConcurrentDictionary<Guid, ClientReadRecord> clientsByGuidCache = [];

    public ClientReadRecord GetById(int id)
    {
        RefreshCacheIfEmpty();
        if (clientsCache.TryGetValue(id, out var record))
        {
            return record;
        }

        throw WbsktExceptions.ClientIdNotExists(id);
    }

    public ClientReadRecord GetByRef(Guid clientRef)
    {
        RefreshCacheIfEmpty();
        if (clientsByGuidCache.TryGetValue(clientRef, out var record))
        {
            return record;
        }

        throw WbsktExceptions.ClientRefNotExists(clientRef);
    }

    public IReadOnlyCollection<ClientReadRecord> GetAll()
    {
        RefreshCacheIfEmpty();
        return [.. clientsCache.Values];
    }

    public IReadOnlyCollection<ClientReadRecord> GetAllByUserId(int userId)
    {
        RefreshCacheIfEmpty();
        return [.. clientsCache.Values.Where(c => c.UserId == userId)];
    }

    public IReadOnlyCollection<ClientReadRecord> GetAllByIds(int[] ids)
    {
        RefreshCacheIfEmpty();
        var result = new List<ClientReadRecord>(ids.Length);

        foreach (var id in ids)
        {
            if (clientsCache.TryGetValue(id, out var record))
            {
                result.Add(record);
            }
        }

        return result.AsReadOnly();
    }

    public IReadOnlyCollection<ClientReadRecord> GetAllByRefs(Guid[] clientRefs)
    {
        RefreshCacheIfEmpty();
        var result = new List<ClientReadRecord>(clientRefs.Length);

        foreach (var clientRef in clientRefs)
        {
            if (clientsByGuidCache.TryGetValue(clientRef, out var record))
            {
                result.Add(record);
            }
        }

        return result.AsReadOnly();
    }

    public void RegisterDatabaseListener()
    {
        if (clientsReader is ClientsDatabaseReader clientsReaderImp)
        {
            clientsReaderImp.RegisterSqlDependency(OnDatabaseChange);
        }
    }

    private void OnDatabaseChange(object sender, SqlNotificationEventArgs e)
    {
        logger.LogInformation("database change detected: {Info}", e.Info);

        RefreshCache();
        RegisterDatabaseListener(); // re-register after change
    }

    private void RefreshCacheIfEmpty()
    {
        if (clientsCache.IsEmpty || clientsByGuidCache.IsEmpty)
        {
            RefreshCache();
        }
    }

    private void RefreshCache()
    {
        try
        {
            var clients = clientsReader.GetAll(_lastModified);
            var maxLastModified = _lastModified;

            foreach (var client in clients)
            {
                clientsCache.AddOrUpdate(client.Id, client, (_, _) => client);
                clientsByGuidCache.AddOrUpdate(client.UniqueRef, client, (_, _) => client);

                if (client.LastModified > maxLastModified)
                {
                    maxLastModified = client.LastModified;
                }
            }

            _lastModified = maxLastModified;
            logger.LogDebug("cache refreshed with {count} clients", clients.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to refresh clients cache");
            throw;
        }
    }
} 