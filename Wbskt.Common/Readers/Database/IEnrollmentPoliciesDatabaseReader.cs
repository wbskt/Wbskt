using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

internal interface IEnrollmentPoliciesDatabaseReader
{
    IReadOnlyCollection<EnrollmentPolicyReadRecord> GetAll(DateTime lastModified);
}
