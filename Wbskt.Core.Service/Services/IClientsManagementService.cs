using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IClientsManagementService
{
    IReadOnlyCollection<ClientReadRecord> GetAll();
    IReadOnlyCollection<ClientReadRecord> GetAllByUserId(int userId);
    ClientReadRecord GetByRef(Guid clientRef);
    IReadOnlyCollection<ClientReadRecord> GetAllByRefs(Guid[] clientRefs);
    int UpsertClient(Guid policyRef, ClientRecord record);
    void SetClientChannels(Guid clientRef, Guid[] channelRefs);
    IReadOnlyCollection<Guid> GetChannelRefsForClient(Guid clientRef);
    IReadOnlyCollection<Guid> GetClientRefsForChannel(Guid channelRef);
}
