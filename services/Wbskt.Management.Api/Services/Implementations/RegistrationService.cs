using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Wbskt.Common;
using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Services;
using Wbskt.Common.Writers;
using Wbskt.Management.Api.Contracts;

namespace Wbskt.Management.Api.Services.Implementations;

internal sealed class RegistrationService : IRegistrationService
{
    private readonly IRegistrationPoliciesReader _policiesReader;
    private readonly IClientsReader _clientsReader;
    private readonly IClientsWriter _clientsWriter;
    private readonly IServersReader _serversReader;
    private readonly IConfiguration _configuration;
    private readonly ICurrentUser _currentUser;

    public RegistrationService(
        IRegistrationPoliciesReader policiesReader,
        IClientsReader clientsReader,
        IClientsWriter clientsWriter,
        IServersReader serversReader,
        IConfiguration configuration,
        ICurrentUser currentUser)
    {
        _policiesReader = policiesReader ?? throw new ArgumentNullException(nameof(policiesReader));
        _clientsReader = clientsReader ?? throw new ArgumentNullException(nameof(clientsReader));
        _clientsWriter = clientsWriter ?? throw new ArgumentNullException(nameof(clientsWriter));
        _serversReader = serversReader ?? throw new ArgumentNullException(nameof(serversReader));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<ClientRegistrationResponse> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken cancellationToken)
    {
        var policy = await _policiesReader.GetByPinAsync(request.Pin, cancellationToken);

        if (policy == null)
        {
            throw WbsktExceptions.InvalidPolicyPin();
        }

        if (policy.Expiry.HasValue && policy.Expiry.Value.ToUniversalTime() < DateTime.UtcNow)
        {
            throw WbsktExceptions.PolicyExpired();
        }

        if (policy.MaxClients.HasValue)
        {
            var clients = await _clientsReader.GetAllByUserIdAsync(policy.UserId, cancellationToken);
            if (clients.Count >= policy.MaxClients.Value)
            {
                throw WbsktExceptions.MaxClientsReached();
            }
        }

        var client = new ClientRecord
        {
            UserId = policy.UserId,
            RegistrationPolicyId = policy.Id,
            Name = request.ClientName,
            RefId = Guid.NewGuid(),
            Active = true,
            LastModified = DateTime.UtcNow
        };

        var clientId = await _clientsWriter.UpsertAsync(client, cancellationToken);

        var server = await GetAvailableServerAsync(cancellationToken);

        var token = GenerateClientToken(clientId, client.RefId, server.PublicDomainName);

        return new ClientRegistrationResponse
        {
            AuthToken = token,
            SocketServerAddress = server.PublicDomainName
        };
    }

    private async Task<ServerRecord> GetAvailableServerAsync(CancellationToken cancellationToken)
    {
        var servers = await _serversReader.GetAllAsync(cancellationToken);

        if (servers.Count == 0)
        {
            throw WbsktExceptions.SocketServerUnavailable();
        }

        // For now, just return the first active server.
        // In the future, we can implement a load balancing strategy here.
        var server = servers.FirstOrDefault(s => s.Status == 1);

        if (server == null)
        {
            throw WbsktExceptions.SocketServerUnavailable();
        }

        return server;
    }

    private string GenerateClientToken(int clientId, Guid clientRefId, string serverAddress)
    {
        var tokenHandler = new JsonWebTokenHandler();
        var configurationKey = _configuration[Constants.JwtKeyNames.ClientServerTokenKey];

        var key = Encoding.UTF8.GetBytes(configurationKey!);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(Constants.Claims.ClientId, clientId.ToString()),
                new Claim(Constants.Claims.ClientUniqueId, clientRefId.ToString()),
                new Claim(Constants.Claims.SocketServer, serverAddress)
            }),
            Expires = DateTime.UtcNow.AddDays(1),
            Issuer = _configuration[Constants.JwtKeyNames.Issuer],
            Audience = _configuration[Constants.JwtKeyNames.Audience],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
        };

        return tokenHandler.CreateToken(tokenDescriptor);
    }
}
