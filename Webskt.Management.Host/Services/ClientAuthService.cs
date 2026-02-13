using Webskt.Common.Abstraction.Models.Management;
using System.Security.Claims;
using Webskt.Common.Security;
using Webskt.Management.Host.Providers;
using Webskt.Common.Abstraction.Exceptions;

namespace Webskt.Management.Host.Services;

internal sealed class ClientAuthService : IClientAuthService
{
    private readonly IClientProvider _provider;
    private readonly IJwtService _jwtService;

    public ClientAuthService(IClientProvider provider, IJwtService jwtService)
    {
        _provider = provider;
        _jwtService = jwtService;
    }

    public async Task<ClientLoginResponse> LoginAsync(ClientLoginRequest request)
    {
        // 1. Verify credentials
        var client = await _provider.VerifyAsync(request.ClientRefId, request.Secret);

        // 2. Check status
        if (client.Status != ClientStatus.Registered)
        {
            throw new SecurityException($"Client registration is {client.Status}. Access denied.");
        }

        // 3. Generate Claims
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, client.RefId.ToString()),
            new Claim(ClaimTypes.Name, client.Name),
            new Claim("policy_ref", client.PolicyRefId.ToString()),
            new Claim("type", "client")
        };

        // 4. Issue Token
        var token = _jwtService.GenerateToken(claims, TimeSpan.FromHours(1));

        return new ClientLoginResponse(token, 3600);
    }
}
