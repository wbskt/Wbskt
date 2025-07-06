using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IChannelsReader
{
    ChannelReadRecord GetById(int channelId);

    IReadOnlyCollection<ChannelReadRecord> GetAll();

    IReadOnlyCollection<ChannelReadRecord> GetAllByUserId(int userId);

    IReadOnlyCollection<ChannelReadRecord> GetAllByIds(int[] ids);

    IReadOnlyCollection<ChannelReadRecord> GetAllByRefs(Guid[] channelRefs);

    ChannelReadRecord GetByRef(Guid channelRef);
}
