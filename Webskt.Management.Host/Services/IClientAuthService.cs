using Webskt.Management.Host.Models;

namespace Webskt.Management.Host.Services;

public interface IClientAuthService
{
    Task<ClientLoginResponse> LoginAsync(ClientLoginRequest request);
}
