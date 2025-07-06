using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

internal interface IClientsDatabaseReader
{
    IReadOnlyCollection<ClientReadRecord> GetAll(DateTime lastModified);
}
