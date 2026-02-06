using System.Security.Cryptography;
using Webskt.Management.Host.Models;
using Webskt.Management.Host.Providers;
using Webskt.Common.Abstraction.Exceptions;

namespace Webskt.Management.Host.Services;

public class ClientRegistrationService : IClientRegistrationService
{
    private readonly IClientProvider _provider;

    public ClientRegistrationService(IClientProvider provider)
    {
        _provider = provider;
    }

    public async Task<ClientRegistrationResponse> InitiateRegistrationAsync(ClientRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Pin))
        {
            throw new ValidationException("Registration PIN is required.");
        }

        // 1. Validate Policy
        var policy = await _provider.GetPolicyByPinAsync(request.Pin);

        // 2. Check Capacity
        if (policy.MaxClients.HasValue)
        {
            var currentCount = await _provider.GetRegisteredCountByPolicyIdAsync(policy.Id);
            
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
        var client = await _provider.InsertClientAsync(policy.Id, request.Name, secret, initialStatus);

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
