using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IServersReader
{
    Task<List<ServerRecord>> GetAllAsync(CancellationToken cancellationToken);
}
