using Webskt.Common.Abstraction.Models.Management;

namespace Webskt.Management.Host.Services;

public interface IClientAuthService
{
    Task<ClientLoginResponse> LoginAsync(ClientLoginRequest request, CancellationToken cancellationToken = default);
}
