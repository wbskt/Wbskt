using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class EnrollmentService(ILogger<EnrollmentService> logger, IEnrollmentPoliciesReader policiesReader, IEnrollmentPoliciesWriter policiesWriter) : IEnrollmentService
{
    public int CreatePolicy(EnrollmentPolicyRecord record)
    {
        // Validate request based on policy type
        ValidatePolicyRequest(record);

        record.PolicyRef = Guid.NewGuid();
        var policyId = policiesWriter.InsertPolicy(record);
        logger.LogDebug("policy created with PolicyRef: {PolicyRef}", record.PolicyRef);
        return policyId;
    }

    public IReadOnlyCollection<EnrollmentPolicyReadRecord> GetPoliciesByUserId(int userId)
    {
        return policiesReader.GetAllByUserId(userId);
    }

    public bool ValidateEnrollmentCode(Guid policyRef, out int policyUserId)
    {
        policyUserId = 0;

        var policy = policiesReader.GetByRef(policyRef);
        if (policy is not { IsActive: true })
        {
            return false;
        }

        // Check policy type specific validations
        switch (policy.PolicyType)
        {
            case EnrollmentPolicyType.TimeLimited:
                if (policy.ExpiryDate.HasValue && DateTime.UtcNow > policy.ExpiryDate.Value)
                {
                    return false;
                }
                break;

            case EnrollmentPolicyType.NumberOfClients:
                if (policy.MaxClients.HasValue && policy.CurrentUsage >= policy.MaxClients.Value)
                {
                    return false;
                }
                break;

            case EnrollmentPolicyType.Unlimited:
                // No restrictions - always valid
                break;

            case EnrollmentPolicyType.TimeAndCount:
                // Check both time and count limits
                if (policy.ExpiryDate.HasValue && DateTime.UtcNow > policy.ExpiryDate.Value)
                {
                    return false;
                }
                if (policy.MaxClients.HasValue && policy.CurrentUsage >= policy.MaxClients.Value)
                {
                    return false;
                }
                break;
        }

        policyUserId = policy.UserId;

        // Increment usage for NumberOfClients and TimeAndCount policies
        if (policy.PolicyType is EnrollmentPolicyType.NumberOfClients or EnrollmentPolicyType.TimeAndCount)
        {
            policiesWriter.IncrementUsage(policy.Id);
        }

        return true;
    }

    private static void ValidatePolicyRequest(EnrollmentPolicyRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
        {
            throw new ArgumentException("Policy name is required", nameof(record));
        }

        switch (record.PolicyType)
        {
            case EnrollmentPolicyType.TimeLimited:
                if (!record.ExpiryDate.HasValue)
                {
                    throw new ArgumentException("Expiry date is required for time-limited policies", nameof(record));
                }
                if (record.ExpiryDate.Value <= DateTime.UtcNow)
                {
                    throw new ArgumentException("Expiry date must be in the future", nameof(record));
                }
                break;

            case EnrollmentPolicyType.NumberOfClients:
                if (record.MaxClients is not > 0)
                {
                    throw new ArgumentException("MaxClients must be greater than 0 for NumberOfClients policies", nameof(record));
                }
                break;

            case EnrollmentPolicyType.Unlimited:
                // No restrictions - always valid
                break;

            case EnrollmentPolicyType.TimeAndCount:
                if (!record.ExpiryDate.HasValue)
                {
                    throw new ArgumentException("Expiry date is required for TimeAndCount policies", nameof(record));
                }
                if (record.ExpiryDate.Value <= DateTime.UtcNow)
                {
                    throw new ArgumentException("Expiry date must be in the future", nameof(record));
                }
                if (record.MaxClients is not > 0)
                {
                    throw new ArgumentException("MaxClients must be greater than 0 for TimeAndCount policies", nameof(record));
                }
                break;

            default:
                throw new ArgumentException("Invalid policy type", nameof(record));
        }
    }
}
