using Wbskt.Management.Api.Contracts;

namespace Wbskt.Management.Api.Services;

public interface IRegistrationService
{
    Task<ClientRegistrationResponse> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken cancellationToken);
}
