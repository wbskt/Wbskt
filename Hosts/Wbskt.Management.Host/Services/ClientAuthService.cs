using System.Security.Claims;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Management.Host.Services;

internal sealed class ClientAuthService : IClientAuthService
{
    private readonly IClientProvider _provider;
    private readonly IJwtService _jwtService;

    public ClientAuthService(IClientProvider provider, IJwtService jwtService)
    {
        _provider = provider;
        _jwtService = jwtService;
    }

    public async Task<ClientLoginResponse> LoginAsync(ClientLoginRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Verify credentials
        var client = await _provider.VerifyAsync(request.ClientRefId, request.Secret, cancellationToken);

        // 2. Check status
        if (client.Status != ClientStatus.Registered)
        {
            throw new SecurityException($"Client registration is {client.Status}. Access denied.");
        }

        // 4. Generate Claims
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, client.RefId.ToString()),
            new Claim(ClaimTypes.Name, client.Name),
            new Claim("id", client.Id.ToString()),
            new Claim("policy_ref", client.PolicyRefId.ToString()),
            new Claim("workspace_id", client.WorkspaceId.ToString()),
            new Claim("type", "client")
        };

        // 5. Issue Token
        var token = _jwtService.GenerateToken(claims, TimeSpan.FromHours(1));

        return new ClientLoginResponse(token, 3600);
    }
}
