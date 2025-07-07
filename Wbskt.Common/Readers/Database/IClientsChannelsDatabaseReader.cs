using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

internal interface IClientsChannelsDatabaseReader
{
    IReadOnlyCollection<ClientChannelReadRecord> GetAll(DateTime lastModified);
}
