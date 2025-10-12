using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

public interface IServersDatabaseReader
{
    Task<List<ServerRecord>> GetAllAsync(DateTime lastModified, CancellationToken cancellationToken);
}
