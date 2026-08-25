using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services;

internal sealed class ClientRegistrationService : IClientRegistrationService
{
    private readonly IClientProvider _clientProvider;
    private readonly IRegistrationPolicyProvider _policyProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ClientRegistrationService> _logger;

    public ClientRegistrationService(
        IClientProvider clientProvider, 
        IRegistrationPolicyProvider policyProvider,
        IEventBus eventBus,
        ILogger<ClientRegistrationService> logger)
    {
        _clientProvider = clientProvider;
        _policyProvider = policyProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<ClientRegistrationResponse>> InitiateRegistrationAsync(ClientRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initiating client registration for Client Name: '{ClientName}'", request.Name);

        if (string.IsNullOrWhiteSpace(request.Pin))
        {
            _logger.LogWarning("Client registration failed: PIN is empty");
            return Result<ClientRegistrationResponse>.Failure(Error.Validation("REGISTRATION_PIN_REQUIRED", "Registration PIN is required."));
        }

        try
        {
            RegistrationPolicy policy;
            try
            {
                policy = await _policyProvider.GetByPinAsync(request.Pin, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Client registration failed: Policy with PIN '{Pin}' not found. Error: {Message}", request.Pin, ex.Message);
                _logger.LogTrace(ex, "GetByPin lookup failure stack trace for PIN '{Pin}'", request.Pin);
                return Result<ClientRegistrationResponse>.Failure(Error.NotFound("POLICY_NOT_FOUND", "Invalid registration PIN."));
            }

            if (!policy.IsEnabled)
            {
                _logger.LogWarning("Client registration failed: Policy '{PolicyName}' is disabled", policy.Name);
                await _eventBus.PublishAsync(new PolicyRegistrationAttemptedOnDisabledEvent(policy.RefId, policy.Id, policy.WorkspaceId, request.Name), cancellationToken);
                return Result<ClientRegistrationResponse>.Failure(Error.Forbidden("POLICY_DISABLED", "This registration policy is currently disabled."));
            }

            if (policy.MaxClients.HasValue)
            {
                var currentCount = await _clientProvider.GetRegisteredCountByPolicyIdAsync(policy.Id, cancellationToken);
                if (currentCount >= policy.MaxClients.Value)
                {
                    _logger.LogWarning("Client registration failed: Limit reached for policy '{PolicyName}' ({Current}/{Max})", policy.Name, currentCount, policy.MaxClients.Value);
                    await _eventBus.PublishAsync(new PolicyRegistrationLimitReachedEvent(policy.RefId, policy.Id, policy.WorkspaceId, policy.MaxClients.Value), cancellationToken);
                    return Result<ClientRegistrationResponse>.Failure(Error.Validation("POLICY_LIMIT_REACHED", "Policy registration limit reached."));
                }
            }

            var secret = ClientSecrets.Generate();
            var initialStatus = policy.AutoApproval ? ClientStatus.Registered : ClientStatus.Pending;

            var client = await _clientProvider.InsertClientAsync(policy.WorkspaceId, policy.Id, request.Name, ClientSecrets.Hash(secret), initialStatus, cancellationToken);
            _logger.LogInformation("Client '{ClientName}' record created with status: '{ClientStatus}'", request.Name, initialStatus);

            await _eventBus.PublishAsync(new ClientRegistrationInitiatedEvent(client.RefId, client.Id, policy.RefId, policy.Id, client.WorkspaceId, client.Name), cancellationToken);

            if (policy.AutoApproval)
            {
                await _eventBus.PublishAsync(new ClientAutoApprovedEvent(client.RefId, client.Id, policy.RefId, policy.Id, client.WorkspaceId), cancellationToken);
            }
            else
            {
                await _eventBus.PublishAsync(new ClientPendingApprovalEvent(client.RefId, client.Id, policy.RefId, policy.Id, client.WorkspaceId), cancellationToken);
            }

            return Result<ClientRegistrationResponse>.Success(new ClientRegistrationResponse(client.RefId, secret, client.Status));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error initiating client registration for '{ClientName}'. Error: {Message}", request.Name, ex.Message);
            _logger.LogTrace(ex, "Client registration exception stack trace for '{ClientName}'", request.Name);
            return Result<ClientRegistrationResponse>.Failure(Error.Failure("REGISTRATION_ERROR", ex.Message));
        }
    }

}
