using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;
using Wbskt.Core.Service.Contracts;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class PolicyService : IPolicyService
{
    private readonly IRegistrationPoliciesReader _policiesReader;
    private readonly IRegistrationPoliciesWriter _policiesWriter;

    public PolicyService(IRegistrationPoliciesReader policiesReader, IRegistrationPoliciesWriter policiesWriter)
    {
        _policiesReader = policiesReader;
        _policiesWriter = policiesWriter;
    }

    public async Task<PolicyResponse> CreatePolicyAsync(int userId, CreatePolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = new RegistrationPolicyRecord
        {
            UserId = userId,
            Name = request.Name,
            MaxClients = request.MaxClients,
            Expiry = request.Expiry,
            Pin = request.Pin,
            RefId = Guid.NewGuid(),
            LastModified = DateTime.UtcNow
        };

        var policyId = await _policiesWriter.InsertAsync(policy, cancellationToken);

        return ToPolicyResponse(policy with { Id = policyId });
    }

    public async Task<List<PolicyResponse>> GetPoliciesAsync(int userId, CancellationToken cancellationToken)
    {
        var policies = await _policiesReader.GetAllAsync(userId, cancellationToken);
        return policies.Select(ToPolicyResponse).ToList();
    }

    public async Task<PolicyResponse?> GetPolicyAsync(int userId, Guid refId, CancellationToken cancellationToken)
    {
        var policy = await _policiesReader.GetByRefIdAsync(refId, cancellationToken);

        if (policy == null || policy.UserId != userId)
        {
            return null;
        }

        return ToPolicyResponse(policy);
    }

    public async Task<PolicyResponse> UpdatePolicyAsync(int userId, Guid refId, UpdatePolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await _policiesReader.GetByRefIdAsync(refId, cancellationToken);

        if (policy == null || policy.UserId != userId)
        {
            throw WbsktExceptions.PolicyNotFound(refId);
        }

        var updatedPolicy = policy with
        {
            Name = request.Name,
            MaxClients = request.MaxClients,
            Expiry = request.Expiry,
            LastModified = DateTime.UtcNow
        };

        await _policiesWriter.UpdateAsync(updatedPolicy, cancellationToken);

        return ToPolicyResponse(updatedPolicy);
    }

    public async Task DeletePolicyAsync(int userId, Guid refId, CancellationToken cancellationToken)
    {
        var policy = await _policiesReader.GetByRefIdAsync(refId, cancellationToken);

        if (policy == null || policy.UserId != userId)
        {
            throw WbsktExceptions.PolicyNotFound(refId);
        }

        await _policiesWriter.DeleteAsync(refId, cancellationToken);
    }

    private static PolicyResponse ToPolicyResponse(RegistrationPolicyRecord policy)
    {
        return new PolicyResponse
        {
            RefId = policy.RefId,
            Name = policy.Name,
            MaxClients = policy.MaxClients,
            Expiry = policy.Expiry,
            Pin = policy.Pin
        };
    }
}
