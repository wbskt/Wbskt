using Wbskt.Common.Records;

namespace Wbskt.Common.Providers.Readers;

public interface IChannelsReader
{
    ChannelReadRecord GetByChannelId(int channelId);

    IReadOnlyCollection<ChannelReadRecord> GetAll();

    IReadOnlyCollection<ChannelReadRecord> GetAllByUserId(int userId);

    IReadOnlyCollection<ChannelReadRecord> GetAllByPublisherId(Guid publisherId);
}
