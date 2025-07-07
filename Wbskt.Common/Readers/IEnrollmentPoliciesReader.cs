using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IEnrollmentPoliciesReader
{
    IReadOnlyCollection<EnrollmentPolicyReadRecord> GetAll();
    IReadOnlyCollection<EnrollmentPolicyReadRecord> GetAllByUserId(int userId);
    EnrollmentPolicyReadRecord? GetByRef(Guid policyRef);
    EnrollmentPolicyReadRecord? GetById(int policyId);
}
