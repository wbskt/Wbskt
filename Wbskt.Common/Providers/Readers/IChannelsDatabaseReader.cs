using Wbskt.Common.Records;

namespace Wbskt.Common.Providers.Readers;

public interface IChannelsDatabaseReader
{
    IReadOnlyCollection<ChannelReadRecord> GetAll(DateTime lastModified);

    IReadOnlyCollection<ChannelReadRecord> GetAllByUserId(DateTime lastModified, int userId);
}
