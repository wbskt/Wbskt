using Webskt.Common.Abstraction.Models.Management;

namespace Webskt.Management.Host.Services;

public interface IClientRegistrationService
{
    Task<ClientRegistrationResponse> InitiateRegistrationAsync(ClientRegistrationRequest request, CancellationToken cancellationToken = default);
}
