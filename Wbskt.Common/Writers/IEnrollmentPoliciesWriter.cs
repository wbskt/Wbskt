using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IEnrollmentPoliciesWriter
{
    int InsertPolicy(EnrollmentPolicyRecord record);
    void IncrementUsage(int policyId);
} 