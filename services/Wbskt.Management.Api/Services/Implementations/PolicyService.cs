using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Services;
using Wbskt.Common.Writers;
using Wbskt.Management.Api.Contracts;

namespace Wbskt.Management.Api.Services.Implementations;

internal sealed class PolicyService : IPolicyService
{
    private readonly IRegistrationPoliciesReader _policiesReader;
    private readonly IRegistrationPoliciesWriter _policiesWriter;
    private readonly ICurrentUser _currentUser;

    public PolicyService(IRegistrationPoliciesReader policiesReader, IRegistrationPoliciesWriter policiesWriter, ICurrentUser currentUser)
    {
        _policiesReader = policiesReader ?? throw new ArgumentNullException(nameof(policiesReader));
        _policiesWriter = policiesWriter ?? throw new ArgumentNullException(nameof(policiesWriter));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<PolicyResponse> CreatePolicyAsync(CreatePolicyRequest request, CancellationToken cancellationToken)
    {
        var pin = await GenerateUniquePin(cancellationToken);

        var policy = new RegistrationPolicyRecord
        {
            UserId = _currentUser.Id,
            Name = request.Name,
            MaxClients = request.MaxClients,
            Expiry = request.Expiry,
            Pin = pin,
            RefId = Guid.NewGuid(),
            LastModified = DateTime.UtcNow
        };

        var policyId = await _policiesWriter.InsertAsync(policy, cancellationToken);

        return ToPolicyResponse(policy with { Id = policyId });
    }

    private async Task<string> GenerateUniquePin(CancellationToken cancellationToken)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        string pin;

        do
        {
            pin = new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
        while (await _policiesReader.GetByPinAsync(pin, cancellationToken) != null);

        return pin;
    }

    public async Task<List<PolicyResponse>> GetPoliciesAsync(CancellationToken cancellationToken)
    {
        var policies = await _policiesReader.GetAllAsync(_currentUser.Id, cancellationToken);
        return policies.Select(ToPolicyResponse).ToList();
    }

    public async Task<PolicyResponse?> GetPolicyAsync(Guid refId, CancellationToken cancellationToken)
    {
        var policy = await _policiesReader.GetByRefIdAsync(_currentUser.Id, refId, cancellationToken);

        if (policy == null)
        {
            return null;
        }

        return ToPolicyResponse(policy);
    }

    public async Task<PolicyResponse> UpdatePolicyAsync(Guid refId, UpdatePolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await _policiesReader.GetByRefIdAsync(_currentUser.Id, refId, cancellationToken);

        if (policy == null)
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

    public async Task DeletePolicyAsync(Guid refId, CancellationToken cancellationToken)
    {
        var policy = await _policiesReader.GetByRefIdAsync(_currentUser.Id, refId, cancellationToken);

        if (policy == null)
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
