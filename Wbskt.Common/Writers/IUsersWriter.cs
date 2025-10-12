using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IUsersWriter
{
    Task<int> InsertAsync(UserRecord user, CancellationToken cancellationToken);
}
