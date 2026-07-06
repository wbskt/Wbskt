using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

public interface IClientAuthService
{
    Task<Result<ClientLoginResponse>> LoginAsync(ClientLoginRequest request, CancellationToken cancellationToken = default);
}
