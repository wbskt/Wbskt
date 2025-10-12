using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

public interface IUserRefreshTokensDatabaseReader
{
    Task<RefreshTokenRecord?> GetByTokenAsync(string token, CancellationToken cancellationToken);
}
