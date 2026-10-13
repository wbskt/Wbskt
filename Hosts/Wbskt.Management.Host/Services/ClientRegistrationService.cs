using Wbskt.Protocol;
using Microsoft.Data.SqlClient;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Telemetry;

namespace Wbskt.Management.Host.Services;

internal sealed class ClientRegistrationService : IClientRegistrationService
{
    /// <summary>Raised by <c>dbo.Client_Create</c> when the policy is already at <c>MaxClients</c>.</summary>
    private const int PolicyLimitReachedError = 50020;

    /// <summary>
    /// Raised by <c>dbo.Client_Create</c> when an auto-approved client would take the workspace past
    /// its ceiling on registered clients.
    /// </summary>
    private const int WorkspaceLimitReachedError = 50025;

    public const int DefaultMaxPerWorkspace = 1000;

    private readonly IClientProvider _clientProvider;
    private readonly IRegistrationPolicyProvider _policyProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ClientRegistrationService> _logger;
    private readonly ManagementMetrics _metrics;
    private readonly IConfiguration? _configuration;

    public ClientRegistrationService(
        IClientProvider clientProvider, 
        IRegistrationPolicyProvider policyProvider,
        IEventBus eventBus,
        ILogger<ClientRegistrationService> logger,
        ManagementMetrics metrics,
        IConfiguration? configuration = null)
    {
        _configuration = configuration;
        _clientProvider = clientProvider;
        _policyProvider = policyProvider;
        _eventBus = eventBus;
        _logger = logger;
        _metrics = metrics;
    }

    private int MaxPerWorkspace => ReadMaxPerWorkspace(_configuration);

    // Registered clients across all of a workspace's policies; pending ones do not count until they
    // are approved, so a leaked PIN for a policy that needs approval cannot fill the workspace.
    // Configuration, not a constant, so it can become a plan tier without a redeploy of the code.
    internal static int ReadMaxPerWorkspace(IConfiguration? configuration) =>
        configuration?.GetValue("Clients:MaxPerWorkspace", DefaultMaxPerWorkspace) ?? DefaultMaxPerWorkspace;

    /// <summary>The answer when a registration or an approval would take the workspace past its ceiling.</summary>
    internal static Error WorkspaceLimitReached(int maxPerWorkspace) => Error.Conflict(
        "WORKSPACE_CLIENT_LIMIT_REACHED",
        $"This workspace can have at most {maxPerWorkspace} devices. Remove one it no longer uses.");

    public async Task<Result<ClientRegistrationResponse>> InitiateRegistrationAsync(ClientRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        var result = await RegisterAsync(request, cancellationToken);
        _metrics.RecordRegistration(result);
        return result;
    }

    private async Task<Result<ClientRegistrationResponse>> RegisterAsync(ClientRegistrationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating client registration for Client Name: '{ClientName}'", request.Name);

        if (string.IsNullOrWhiteSpace(request.Pin))
        {
            _logger.LogWarning("Client registration failed: PIN is empty");
            return Result<ClientRegistrationResponse>.Failure(Error.Validation("REGISTRATION_PIN_REQUIRED", "Registration PIN is required."));
        }

        var policy = await _policyProvider.FindByPinAsync(request.Pin, cancellationToken);
        if (policy is null)
        {
            // The presented PIN is never logged: a wrong guess is noise, and a right one is a credential.
            _logger.LogWarning("Client registration failed: no policy matches the presented PIN");
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

        Client client;
        try
        {
            client = await _clientProvider.InsertClientAsync(policy.WorkspaceId, policy.Id, request.Name, ClientSecrets.Hash(secret), initialStatus, MaxPerWorkspace, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == WorkspaceLimitReachedError)
        {
            _logger.LogWarning("Client registration failed: WorkspaceId {WorkspaceId} is at its limit of {MaxClients} clients", policy.WorkspaceId, MaxPerWorkspace);
            return Result<ClientRegistrationResponse>.Failure(WorkspaceLimitReached(MaxPerWorkspace));
        }
        catch (SqlException ex) when (ex.Number == PolicyLimitReachedError && policy.MaxClients.HasValue)
        {
            // A concurrent registration took the last slot after the count above. Client_Create
            // re-checks under a lock on the policy row; this is that check refusing.
            _logger.LogWarning("Client registration failed: Limit reached for policy '{PolicyName}' (concurrent registration)", policy.Name);
            await _eventBus.PublishAsync(new PolicyRegistrationLimitReachedEvent(policy.RefId, policy.Id, policy.WorkspaceId, policy.MaxClients.Value), cancellationToken);
            return Result<ClientRegistrationResponse>.Failure(Error.Validation("POLICY_LIMIT_REACHED", "Policy registration limit reached."));
        }

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

}
