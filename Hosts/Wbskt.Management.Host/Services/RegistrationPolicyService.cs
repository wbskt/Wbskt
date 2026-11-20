using System.Globalization;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

internal sealed class RegistrationPolicyService : IRegistrationPolicyService
{
    private readonly IRegistrationPolicyProvider _provider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<RegistrationPolicyService> _logger;

    public RegistrationPolicyService(
        IRegistrationPolicyProvider provider,
        IEventBus eventBus,
        ILogger<RegistrationPolicyService> logger)
    {
        _provider = provider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<IPagedList<RegistrationPolicyResponse>>> GetAllAsync(int workSpaceId, bool? autoApproval,
        string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying registration policies for WorkspaceId: {WorkspaceId}", workSpaceId);

        var pagedPolicies = await _provider.GetAllAsync(workSpaceId, autoApproval, name, skip, take, cancellationToken);
        _logger.LogTrace("Retrieved {Count} policies for WorkspaceId: {WorkspaceId}", pagedPolicies.TotalCount, workSpaceId);
        
        var result = new PagedList<RegistrationPolicyResponse>(pagedPolicies.Select(MapToResponse), pagedPolicies.TotalCount);
        return Result<IPagedList<RegistrationPolicyResponse>>.Success(result);
    }

    public async Task<Result<RegistrationPolicy>> FindInWorkspaceAsync(int workspaceId, Guid refId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying registration policy '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var lookup = await WorkspaceOwnership.LoadAsync(workspaceId, () => _provider.FindByRefIdAsync(refId, cancellationToken), WorkspaceOwnership.PolicyNotFound);
        if (lookup.IsFailure)
        {
            _logger.LogWarning("Registration policy '{RefId}' not found in WorkspaceId: {WorkspaceId}", refId, workspaceId);
        }

        return lookup;
    }

    public async Task<Result<RegistrationPolicyResponse>> GetAsync(int workspaceId, Guid policyRefId, CancellationToken cancellationToken = default)
    {
        var policy = await FindInWorkspaceAsync(workspaceId, policyRefId, cancellationToken);
        return policy.IsSuccess
            ? Result<RegistrationPolicyResponse>.Success(MapToResponse(policy.Value))
            : Result<RegistrationPolicyResponse>.Failure(policy.Error);
    }

    public async Task<Result<RegistrationPolicyResponse>> CreateAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating registration policy '{PolicyName}' in WorkspaceId: {WorkspaceId}", request.Name, workspaceId);

        var invalid = CheckName(request.Name) ?? CheckLimit(request.MaxClients);
        if (invalid is not null)
        {
            _logger.LogWarning("Registration policy creation failed: {Code}", invalid.Code);
            return Result<RegistrationPolicyResponse>.Failure(invalid);
        }

        var policy = await _provider.InsertAsync(workspaceId, request with { Name = request.Name.Trim() }, cancellationToken);
        _logger.LogInformation("Registration policy '{PolicyName}' created successfully with RefId: {RefId}", request.Name, policy.RefId);
        
        await _eventBus.PublishAsync(new PolicyCreatedEvent(policy.RefId, policy.Id, workspaceId, policy.Name), cancellationToken);

        return Result<RegistrationPolicyResponse>.Success(MapToResponse(policy));
    }

    public async Task<Result> UpdateAsync(int workspaceId, Guid policyRefId, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating registration policy '{PolicyRefId}' in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);

        if (request is { Name: null, MaxClients: null, AutoApproval: null, IsEnabled: null, RemoveMaxClients: false })
        {
            return Result.Failure(Error.Validation("POLICY_UPDATE_EMPTY", "Name a field to change: 'name', 'maxClients', 'removeMaxClients', 'autoApproval' or 'isEnabled'."));
        }

        if (request.MaxClients.HasValue && request.RemoveMaxClients)
        {
            return Result.Failure(Error.Validation("POLICY_LIMIT_AMBIGUOUS", "Send either 'maxClients' or 'removeMaxClients', not both."));
        }

        var invalid = (request.Name is null ? null : CheckName(request.Name)) ?? CheckLimit(request.MaxClients);
        if (invalid is not null)
        {
            _logger.LogWarning("Registration policy update failed: {Code}", invalid.Code);
            return Result.Failure(invalid);
        }

        request = request with { Name = request.Name?.Trim() };

        var lookup = await WorkspaceOwnership.LoadAsync(workspaceId, () => _provider.FindByRefIdAsync(policyRefId, cancellationToken), WorkspaceOwnership.PolicyNotFound);
        if (lookup.IsFailure)
        {
            _logger.LogWarning("Policy update rejected: Policy '{PolicyRefId}' not found in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);
            return Result.Failure(lookup.Error);
        }

        var policy = lookup.Value;
        var policyId = policy.Id;

        if (await _provider.UpdateAsync(workspaceId, policyId, request, cancellationToken) is { } registered)
        {
            _logger.LogWarning("Policy update failed: {Current} registered clients exceed the new limit ({Max}) for Policy ID {PolicyId}", registered, request.MaxClients, policyId);
            return Result.Failure(Error.Validation("POLICY_LIMIT_CONFLICT", $"Cannot set max clients to {request.MaxClients} because {registered} clients are already registered under this policy."));
        }

        _logger.LogInformation("Registration policy ID {PolicyId} updated successfully", policyId);

        var updatedPolicy = await _provider.FindByIdAsync(policyId, cancellationToken)
            ?? throw new InvalidOperationException($"Policy {policyId} vanished during its own update.");

        // Compared against the policy as it was before the update. Comparing the re-read row with the
        // request (as this once did) found them equal every time, so no change was ever announced.
        var events = new List<BaseEvent>(3);
        if (!string.Equals(policy.Name, updatedPolicy.Name, StringComparison.Ordinal))
        {
            events.Add(new PolicyNameUpdatedEvent(updatedPolicy.RefId, updatedPolicy.Id, workspaceId, updatedPolicy.Name, policy.Name)
            {
                Changes = [new FieldChange("name", policy.Name, updatedPolicy.Name)]
            });
        }

        if (policy.MaxClients != updatedPolicy.MaxClients)
        {
            events.Add(new PolicyClientLimitUpdatedEvent(updatedPolicy.RefId, updatedPolicy.Id, workspaceId, updatedPolicy.MaxClients, policy.MaxClients)
            {
                Changes = [new FieldChange("clientLimit", policy.MaxClients?.ToString(CultureInfo.InvariantCulture), updatedPolicy.MaxClients?.ToString(CultureInfo.InvariantCulture))]
            });
        }

        if (policy.IsEnabled && !updatedPolicy.IsEnabled)
        {
            events.Add(Disabled(policy, workspaceId));
        }
        else if (!policy.IsEnabled && updatedPolicy.IsEnabled)
        {
            events.Add(new PolicyEnabledEvent(updatedPolicy.RefId, updatedPolicy.Id, workspaceId) { Changes = [new FieldChange("enabled", "false", "true")] });
        }

        await Parallel.ForEachAsync(events, cancellationToken, async (@event, token) => await _eventBus.PublishAsync(@event, token));

        return Result.Success();
    }

    public async Task<Result<RegistrationPolicyResponse>> RotatePinAsync(int workspaceId, Guid policyRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rotating PIN of registration policy '{PolicyRefId}' in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);

        var lookup = await WorkspaceOwnership.LoadAsync(workspaceId, () => _provider.FindByRefIdAsync(policyRefId, cancellationToken), WorkspaceOwnership.PolicyNotFound);
        if (lookup.IsFailure)
        {
            _logger.LogWarning("PIN rotation rejected: Policy '{PolicyRefId}' not found in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);
            return Result<RegistrationPolicyResponse>.Failure(lookup.Error);
        }

        var policy = lookup.Value;
        var policyId = policy.Id;

        var rotated = await _provider.RotatePinAsync(workspaceId, policyId, cancellationToken);
        _logger.LogInformation("Registration policy ID {PolicyId} has a new PIN", policyId);

        await _eventBus.PublishAsync(new PolicyPinRotatedEvent(rotated.RefId, rotated.Id, workspaceId), cancellationToken);

        return Result<RegistrationPolicyResponse>.Success(MapToResponse(rotated));
    }

    public async Task<Result> DeleteAsync(int workspaceId, Guid policyRefId, CancellationToken cancellationToken = default)
    {
        var lookup = await FindInWorkspaceAsync(workspaceId, policyRefId, cancellationToken);
        if (lookup.IsFailure)
        {
            return Result.Failure(lookup.Error);
        }

        var policy = lookup.Value;
        var outcome = await _provider.DeleteAsync(workspaceId, policy.Id, cancellationToken);
        if (outcome.ClientCount > 0)
        {
            _logger.LogInformation("Policy delete refused: policy ID {PolicyId} still has {ClientCount} clients", policy.Id, outcome.ClientCount);
            return Result.Failure(Error.Conflict(
                "POLICY_HAS_CLIENTS",
                string.Create(CultureInfo.InvariantCulture, $"This policy still has {outcome.ClientCount} {(outcome.ClientCount == 1 ? "client" : "clients")}. Move or delete them first.")));
        }

        if (!outcome.Deleted)
        {
            // Deleted by another request between the lookup and the delete.
            return Result.Failure(WorkspaceOwnership.PolicyNotFound);
        }

        _logger.LogInformation("Registration policy ID {PolicyId} deleted", policy.Id);
        await _eventBus.PublishAsync(new PolicyDeletedEvent(policy.RefId, policy.Id, workspaceId, policy.Name), cancellationToken);
        return Result.Success();
    }

    private static RegistrationPolicyResponse MapToResponse(RegistrationPolicy p)
    {
        return new RegistrationPolicyResponse(
            p.RefId,
            p.Pin,
            p.Name,
            p.MaxClients,
            p.AutoApproval,
            p.IsEnabled,
            p.CreatedAt.ToUtcOffset(),
            p.RegisteredClientCount,
            p.ConnectedClientCount,
            p.PinRotatedAt.ToUtcOffset()
        );
    }

    /// <summary>The event for disabling <paramref name="before"/>, the policy as it was until now.</summary>
    private static PolicyDisabledEvent Disabled(RegistrationPolicy before, int workspaceId) =>
        new(before.RefId, before.Id, workspaceId) { Changes = [new FieldChange("enabled", before.IsEnabled ? "true" : "false", "false")] };

    /// <summary>A blank name, or one longer than its column, which SQL would cut without a word.</summary>
    private static Error? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameRequired;
        }

        return EntityNames.Normalize(name) is null ? NameTooLong : null;
    }

    /// <summary>A limit below one would refuse every device; no limit is null.</summary>
    private static Error? CheckLimit(int? maxClients) => maxClients is < 1 ? LimitInvalid : null;

    private static readonly Error NameRequired = Error.Validation("POLICY_NAME_REQUIRED", "Policy name is required.");
    private static readonly Error NameTooLong = Error.Validation("POLICY_NAME_TOO_LONG", "Policy name must be at most 100 characters.");
    private static readonly Error LimitInvalid = Error.Validation("POLICY_LIMIT_INVALID", "'maxClients' must be at least 1, or left out for no limit.");
}
