using System.Security.Claims;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Management.Host.Services;

internal sealed class ClientAuthService : IClientAuthService
{
    private readonly IClientProvider _provider;
    private readonly IJwtService _jwtService;
    private readonly ILogger<ClientAuthService> _logger;

    public ClientAuthService(IClientProvider provider, IJwtService jwtService, ILogger<ClientAuthService> logger)
    {
        _provider = provider;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<Result<ClientLoginResponse>> LoginAsync(ClientLoginRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Client login attempt for ClientRefId: {ClientRefId}", request.ClientRefId);

        try
        {
            ClientCredential credential;
            try
            {
                credential = await _provider.GetCredentialAsync(request.ClientRefId, cancellationToken);
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Client credentials verification failed for ClientRefId: {ClientRefId}. Error: {Message}", request.ClientRefId, ex.Message);
                _logger.LogTrace(ex, "Client credentials verification failure stack trace for ClientRefId {ClientRefId}", request.ClientRefId);
                return Result<ClientLoginResponse>.Failure(Error.Unauthorized("CLIENT_UNAUTHORIZED", "Invalid client credentials."));
            }

            // The comparison the database used to do, moved here: the stored value is a hash now, and
            // it is checked in fixed time rather than by SQL's `=`, which is neither constant-time nor
            // case-sensitive under the default collation.
            if (!ClientSecrets.Matches(credential.SecretHash, request.Secret))
            {
                _logger.LogWarning("Client credentials verification failed for ClientRefId: {ClientRefId}. Secret did not match.", request.ClientRefId);
                return Result<ClientLoginResponse>.Failure(Error.Unauthorized("CLIENT_UNAUTHORIZED", "Invalid client credentials."));
            }

            Client client = credential.Client;

            if (client.Status != ClientStatus.Registered)
            {
                _logger.LogWarning("Client login rejected: Client status is '{ClientStatus}' instead of 'Registered'. ClientRefId: {ClientRefId}", client.Status, request.ClientRefId);
                return Result<ClientLoginResponse>.Failure(Error.Unauthorized("CLIENT_NOT_REGISTERED", $"Client registration is {client.Status}. Access denied."));
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, client.RefId.ToString()),
                new Claim(ClaimTypes.Name, client.Name),
                new Claim("id", client.Id.ToString()),
                new Claim("policy_ref", client.PolicyRefId.ToString()),
                new Claim("workspace_id", client.WorkspaceId.ToString()),
                new Claim("type", "client")
            };

            var token = _jwtService.GenerateToken(claims, JwtAudiences.Socket, TimeSpan.FromHours(1));
            _logger.LogInformation("Client logged in successfully. ClientRefId: {ClientRefId}, ClientId: {ClientId}", request.ClientRefId, client.Id);

            return Result<ClientLoginResponse>.Success(new ClientLoginResponse(token, 3600));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error during client login for ClientRefId: {ClientRefId}. Error: {Message}", request.ClientRefId, ex.Message);
            _logger.LogTrace(ex, "Client login exception stack trace for ClientRefId {ClientRefId}", request.ClientRefId);
            return Result<ClientLoginResponse>.Failure(Error.Failure("CLIENT_LOGIN_ERROR", ex.Message));
        }
    }
}
