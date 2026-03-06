using Wbskt.Common.Abstraction.Models.Management;

namespace Wbskt.Management.Host.Services;

public interface IClientAuthService
{
    Task<ClientLoginResponse> LoginAsync(ClientLoginRequest request, CancellationToken cancellationToken = default);
}
