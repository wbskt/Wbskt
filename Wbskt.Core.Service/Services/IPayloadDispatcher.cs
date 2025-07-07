using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IPayloadDispatcher
{
    Task<bool> DispatchPayload(ClientPayload payload);
}
