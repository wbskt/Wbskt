using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

public interface IClientRegistrationService
{
    Task<Result<ClientRegistrationResponse>> InitiateRegistrationAsync(ClientRegistrationRequest request, CancellationToken cancellationToken = default);
}
