using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IEnrollmentService
{
    int CreatePolicy(EnrollmentPolicyRecord record);
    IReadOnlyCollection<EnrollmentPolicyReadRecord> GetPoliciesByUserId(int userId);
    bool ValidateEnrollmentCode(Guid policyRef, out int policyUserId);
}
