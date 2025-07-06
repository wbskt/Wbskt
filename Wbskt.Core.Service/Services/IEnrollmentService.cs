using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IEnrollmentService
{
    EnrollmentPolicyReadRecord CreatePolicy(EnrollmentPolicyRecord record);
    IReadOnlyCollection<EnrollmentPolicyReadRecord> GetPoliciesByUserId(int userId);
    bool ValidateEnrollmentCode(string policyRef, out int policyUserId);
}
