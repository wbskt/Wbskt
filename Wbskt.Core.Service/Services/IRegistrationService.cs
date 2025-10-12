using Wbskt.Core.Service.Contracts;

namespace Wbskt.Core.Service.Services;

public interface IRegistrationService
{
    Task<ClientRegistrationResponse> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken cancellationToken);
}
