using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

public interface IClientRegistrationService
{
    Task<ClientRegistrationResponse> InitiateRegistrationAsync(ClientRegistrationRequest request, CancellationToken cancellationToken = default);
}
