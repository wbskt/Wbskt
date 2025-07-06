using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class EnrollmentService(IEnrollmentPoliciesReader policiesReader, IEnrollmentPoliciesWriter policiesWriter) : IEnrollmentService
{
    public EnrollmentPolicyReadRecord CreatePolicy(EnrollmentPolicyRecord record)
    {
        // Validate request based on policy type
        ValidatePolicyRequest(record);

        // Generate PolicyRef if not provided
        var policyRecord = record.PolicyRef == Guid.Empty 
            ? record with { PolicyRef = Guid.NewGuid() }
            : record;

        var policyId = policiesWriter.InsertPolicy(policyRecord);

        // Get the created policy to return the full record
        var createdPolicy = policiesReader.GetAllByUserId(record.UserId)
            .FirstOrDefault(p => p.PolicyRef == policyRecord.PolicyRef);

        if (createdPolicy == null)
        {
            throw new InvalidOperationException("Failed to retrieve created enrollment policy");
        }

        return createdPolicy;
    }

    public IReadOnlyCollection<EnrollmentPolicyReadRecord> GetPoliciesByUserId(int userId)
    {
        return policiesReader.GetAllByUserId(userId);
    }

    public bool ValidateEnrollmentCode(string policyRef, out int policyUserId)
    {
        policyUserId = 0;

        if (string.IsNullOrWhiteSpace(policyRef) || !Guid.TryParse(policyRef, out var guid))
        {
            return false;
        }

        var policy = policiesReader.GetByCode(policyRef);
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
            case EnrollmentPolicyType.SingleUse:
                if (policy.MaxClients.HasValue && policy.CurrentUsage >= policy.MaxClients.Value)
                {
                    return false;
                }
                break;
        }

        policyUserId = policy.UserId;
        
        // Increment usage for NumberOfClients and SingleUse policies
        if (policy.PolicyType is EnrollmentPolicyType.NumberOfClients or EnrollmentPolicyType.SingleUse)
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

            case EnrollmentPolicyType.SingleUse:
                if (record.MaxClients.HasValue && record.MaxClients.Value != 1)
                {
                    throw new ArgumentException("MaxClients must be 1 for SingleUse policies", nameof(record));
                }
                break;

            default:
                throw new ArgumentException("Invalid policy type", nameof(record));
        }
    }
}
