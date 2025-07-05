using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IChannelsWriter
{
    int InsertChannel(ChannelRecord record);
}
