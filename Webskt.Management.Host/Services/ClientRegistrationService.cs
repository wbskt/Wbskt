using Webskt.Common.Abstraction.Models.Management;
using System.Security.Cryptography;
using Webskt.Management.Host.Providers;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Events;
using Webskt.Common.Abstraction.Events.Shared;

namespace Webskt.Management.Host.Services;

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

        // 2. Check Capacity
        if (policy.MaxClients.HasValue)
        {
            var currentCount = await _clientProvider.GetRegisteredCountByPolicyIdAsync(policy.Id, cancellationToken);
            
            // Logic requiring an empty line before this comment
            if (currentCount >= policy.MaxClients.Value)
            {
                throw new ValidationException("Policy registration limit reached.");
            }
        }

        // 3. Generate Credentials
        var secret = GenerateSecret();
        var initialStatus = policy.AutoApproval ? ClientStatus.Registered : ClientStatus.Pending;

        // 4. Create Client
        var client = await _clientProvider.InsertClientAsync(policy.Id, request.Name, secret, initialStatus, cancellationToken);

        // 5. Publish Events
        await _eventBus.PublishAsync(new ClientRegistrationInitiatedEvent(client.RefId, policy.RefId, client.Name), cancellationToken);

        if (policy.AutoApproval)
        {
            await _eventBus.PublishAsync(new ClientStatusChangedEvent(client.RefId, ClientStatus.Pending, ClientStatus.Registered), cancellationToken);
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
