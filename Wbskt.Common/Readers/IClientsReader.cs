using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IClientsReader
{
    ClientReadRecord GetById(int id);
    ClientReadRecord GetByRef(Guid clientRef);
    IReadOnlyCollection<ClientReadRecord> GetAll();
    IReadOnlyCollection<ClientReadRecord> GetAllByUserId(int userId);
    IReadOnlyCollection<ClientReadRecord> GetAllByIds(int[] ids);
    IReadOnlyCollection<ClientReadRecord> GetAllByRefs(Guid[] clientRefs);
}
