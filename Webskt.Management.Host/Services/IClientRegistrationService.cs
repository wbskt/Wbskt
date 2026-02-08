using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Models;

namespace Webskt.Management.Host.Services;

public interface IClientRegistrationService
{
    Task<ClientRegistrationResponse> InitiateRegistrationAsync(ClientRegistrationRequest request);
}
