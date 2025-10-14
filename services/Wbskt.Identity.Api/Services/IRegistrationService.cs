using Wbskt.Identity.Api.Contracts;

namespace Wbskt.Identity.Api.Services;

public interface IRegistrationService
{
    Task<ClientRegistrationResponse> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken cancellationToken);
}
