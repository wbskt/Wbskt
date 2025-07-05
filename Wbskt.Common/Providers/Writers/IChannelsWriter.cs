using Wbskt.Common.Records;

namespace Wbskt.Common.Providers.Writers;

public interface IChannelsWriter
{
    int InsertChannel(ChannelRecord record);
}
