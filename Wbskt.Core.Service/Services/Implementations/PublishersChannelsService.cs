using Wbskt.Common.Readers;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

public class PublishersChannelsService(ILogger<PublishersChannelsService> logger, IPublishersChannelsWriter publishersChannelsWriter, IPublishersChannelsReader publishersChannelsReader) : IPublishersChannelsService
{
}
