using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IUsersReader
{
    Task<UserRecord?> GetByIdAsync(int userId, CancellationToken cancellationToken);
    Task<UserRecord?> GetByEmailIdAsync(string emailId, CancellationToken cancellationToken);
    Task<int> FindByEmailIdAsync(string emailId, CancellationToken cancellationToken);
}
