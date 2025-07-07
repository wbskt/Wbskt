using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IClientsWriter
{
    int UpsertClient(ClientRecord record);
}
