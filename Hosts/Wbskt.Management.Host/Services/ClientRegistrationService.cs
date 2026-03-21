using System.Security.Cryptography;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Foundation.Abstraction.Exceptions;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services;

internal sealed class ClientRegistrationService : IClientRegistrationService
{
    private readonly IClientProvider _clientProvider;
    private readonly IRegistrationPolicyProvider _policyProvider;
    private readonly IEventBus _eventBus;

    public ClientRegistrationService(
        IClientProvider clientProvider, 
        IRegistrationPolicyProvider policyProvider,
        IEventBus eventBus)
    {
        _clientProvider = clientProvider;
        _policyProvider = policyProvider;
        _eventBus = eventBus;
    }

    public async Task<ClientRegistrationResponse> InitiateRegistrationAsync(ClientRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Pin))
        {
            throw new ValidationException("Registration PIN is required.");
        }

        // 1. Validate Policy
        var policy = await _policyProvider.GetByPinAsync(request.Pin, cancellationToken);

        if (!policy.IsEnabled)
        {
            await _eventBus.PublishAsync(new PolicyRegistrationAttemptedOnDisabledEvent(policy.RefId, policy.Id, policy.WorkspaceId, request.Name), cancellationToken);
            throw new SecurityException("This registration policy is currently disabled.");
        }

        // 2. Check Capacity
        if (policy.MaxClients.HasValue)
        {
            var currentCount = await _clientProvider.GetRegisteredCountByPolicyIdAsync(policy.Id, cancellationToken);

            // Logic requiring an empty line before this comment
            if (currentCount >= policy.MaxClients.Value)
            {
                await _eventBus.PublishAsync(new PolicyRegistrationLimitReachedEvent(policy.RefId, policy.Id, policy.WorkspaceId, policy.MaxClients.Value), cancellationToken);
                throw new ValidationException("Policy registration limit reached.");
            }
        }

        // 3. Generate Credentials
        var secret = GenerateSecret();
        var initialStatus = policy.AutoApproval ? ClientStatus.Registered : ClientStatus.Pending;

        // 4. Create Client
        var client = await _clientProvider.InsertClientAsync(policy.WorkspaceId ,policy.Id, request.Name, secret, initialStatus, cancellationToken);

        // 5. Publish Events
        await _eventBus.PublishAsync(new ClientRegistrationInitiatedEvent(client.RefId, client.Id, policy.RefId, policy.Id, client.WorkspaceId, client.Name), cancellationToken);

        if (policy.AutoApproval)
        {
            await _eventBus.PublishAsync(new ClientAutoApprovedEvent(client.RefId, client.Id, policy.RefId, policy.Id, client.WorkspaceId), cancellationToken);
        }
        else
        {
            await _eventBus.PublishAsync(new ClientPendingApprovalEvent(client.RefId, client.Id, policy.RefId, policy.Id, client.WorkspaceId), cancellationToken);
        }

        return new ClientRegistrationResponse(
            client.RefId,
            client.Secret,
            client.Status
        );
    }

    private static string GenerateSecret()
    {
        var buffer = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);

        return Convert.ToBase64String(buffer);
    }
}
