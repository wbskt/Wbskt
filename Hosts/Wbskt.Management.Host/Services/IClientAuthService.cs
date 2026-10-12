using Wbskt.Protocol;
using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Services;

public interface IClientAuthService
{
    Task<Result<ClientLoginResponse>> LoginAsync(ClientLoginRequest request, CancellationToken cancellationToken = default);
}
