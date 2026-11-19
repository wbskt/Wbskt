using Wbskt.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Changes to a client: its status, name and tags, its secret, and deleting it. Each change that
/// affects a connected client also tells the socket host, through <see cref="ClientAccessRevoker"/>.
/// </summary>
internal sealed class ClientLifecycleService : IClientLifecycleService
{
    private readonly IClientProvider _clientProvider;
    private readonly IEventBus _eventBus;
    private readonly ClientAccessRevoker _access;
    private readonly ILogger<ClientLifecycleService> _logger;
    private readonly IWorkspacePlans _plans;

    public ClientLifecycleService(
        IClientProvider clientProvider,
        IEventBus eventBus,
        IClientTokenCutoffs cutoffs,
        ILogger<ClientLifecycleService> logger,
        IWorkspacePlans plans,
        TimeProvider time)
    {
        _plans = plans;
        _clientProvider = clientProvider;
        _eventBus = eventBus;
        _access = new ClientAccessRevoker(eventBus, cutoffs, time);
        _logger = logger;
    }

    public async Task<Result> UpdateStatusAsync(int workspaceId, Guid clientRefId, ClientStatus status, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating client RefId {ClientRefId} status to '{ClientStatus}' in WorkspaceId: {WorkspaceId}", clientRefId, status, workspaceId);
        if (!Enum.IsDefined(status))
        {
            return Result.Failure(StatusInvalid);
        }

        // The same limit registration enforces: approving a pending or revoked client makes it count.
        WorkspacePlan plan = await _plans.GetAsync(workspaceId, cancellationToken);
        PlanCeiling ceiling = plan.ClientCeiling;
        var change = (await _clientProvider.UpdateStatusesAsync(workspaceId, [clientRefId], status, ceiling, cancellationToken)).Single();
        switch (change.Outcome)
        {
            case ClientStatusOutcome.NotFound or ClientStatusOutcome.OtherWorkspace:
                _logger.LogWarning("Failed to update client status: RefId {ClientRefId} not found in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);
                return Result.Failure(WorkspaceOwnership.ClientNotFound);
            case ClientStatusOutcome.PolicyLimitReached:
                _logger.LogWarning("Client approval failed: Policy registration limit reached for Policy ID {PolicyId}", change.PolicyId);
                return Result.Failure(PolicyFull);
            case ClientStatusOutcome.PlanLimitReached:
                _logger.LogWarning("Client approval failed: WorkspaceId {WorkspaceId} is at its plan's limit of {MaxClients} clients", workspaceId, ceiling.Limit);
                int current = await _clientProvider.GetRegisteredCountByAccountAsync(workspaceId, plan.TenantId, cancellationToken);
                await AnnouncePlanFullAsync(workspaceId, change, plan, current, cancellationToken);
                return Result.Failure(ClientRegistrationService.PlanLimitReached(plan, current));
            case ClientStatusOutcome.Updated:
                await AnnounceStatusChangeAsync(workspaceId, change, status, cancellationToken);
                break;
        }

        return Result.Success();
    }

    public async Task<Result<BulkClientStatusResponse>> UpdateStatusesAsync(int workspaceId, IReadOnlyList<Guid> clientRefIds,
        ClientStatus status, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating {Count} clients to status '{ClientStatus}' in WorkspaceId: {WorkspaceId}", clientRefIds.Count, status, workspaceId);
        if (!Enum.IsDefined(status))
        {
            return Result<BulkClientStatusResponse>.Failure(StatusInvalid);
        }

        // One database call for the whole list, in the order given: approvals compete for the
        // policy's and the workspace's remaining places, and a predictable order decides which ones get them.
        WorkspacePlan plan = await _plans.GetAsync(workspaceId, cancellationToken);
        PlanCeiling ceiling = plan.ClientCeiling;
        IReadOnlyList<ClientStatusChange> changes = await _clientProvider.UpdateStatusesAsync(workspaceId, clientRefIds.Distinct().ToList(), status, ceiling, cancellationToken);

        Error? planFull = null;
        if (changes.FirstOrDefault(c => c.Outcome == ClientStatusOutcome.PlanLimitReached) is { } firstRefused)
        {
            // Announced once for the call, not once per refused client: the limit and the count are the same for all of them.
            int current = await _clientProvider.GetRegisteredCountByAccountAsync(workspaceId, plan.TenantId, cancellationToken);
            await AnnouncePlanFullAsync(workspaceId, firstRefused, plan, current, cancellationToken);
            planFull = ClientRegistrationService.PlanLimitReached(plan, current);
        }
        var updated = new List<Guid>();
        var failed = new List<BulkClientStatusFailure>();
        foreach (var change in changes)
        {
            Error? error = change.Outcome switch
            {
                // A missing client and another workspace's read the same; see WorkspaceOwnership.
                ClientStatusOutcome.NotFound or ClientStatusOutcome.OtherWorkspace => WorkspaceOwnership.ClientNotFound,
                ClientStatusOutcome.PolicyLimitReached => PolicyFull,
                ClientStatusOutcome.PlanLimitReached => planFull,
                _ => null
            };

            if (error is null && change.Outcome == ClientStatusOutcome.Updated)
            {
                try
                {
                    await AnnounceStatusChangeAsync(workspaceId, change, status, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The change is committed, so the client is listed as updated; one failed
                    // announcement must not stop the rest of the list from being announced.
                    _logger.LogError(ex, "Client RefId {ClientRefId} changed status but announcing it failed.", change.RefId);
                }
            }

            if (error is null)
            {
                updated.Add(change.RefId);
            }
            else
            {
                failed.Add(new BulkClientStatusFailure(change.RefId, error.Code, error.Message));
            }
        }

        return Result<BulkClientStatusResponse>.Success(new BulkClientStatusResponse(updated, failed));
    }

    private Task AnnouncePlanFullAsync(int workspaceId, ClientStatusChange refused, WorkspacePlan plan, int current, CancellationToken cancellationToken) =>
        _eventBus.PublishAsync(new PlanClientLimitReachedEvent(refused.PolicyRefId, refused.PolicyId, workspaceId, plan.PlanCode.ToString(), plan.Limits.Clients, current, refused.RefId, ClientName: null), cancellationToken);

    private async Task AnnounceStatusChangeAsync(int workspaceId, ClientStatusChange change, ClientStatus status, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updated client ID {ClientId} status from '{OldStatus}' to '{NewStatus}'", change.Id, change.OldStatus, status);
        await _access.StatusChangedAsync(workspaceId, change, status, cancellationToken);
    }

    public async Task<Result> DeleteAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting client RefId {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        var lookup = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (lookup.IsFailure)
        {
            return Result.Failure(lookup.Error);
        }

        var client = lookup.Value;

        if (!await _clientProvider.DeleteAsync(client.Id, workspaceId, cancellationToken))
        {
            // Deleted by someone else between the lookup and here: the outcome they asked for.
            return Result.Success();
        }

        _logger.LogInformation("Deleted client ID {ClientId} ('{Name}') from WorkspaceId: {WorkspaceId}", client.Id, client.Name, workspaceId);
        await _access.DeletedAsync(client, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<ClientSecretResponse>> RotateSecretAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rotating secret for client RefId {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        var lookup = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (lookup.IsFailure)
        {
            return Result<ClientSecretResponse>.Failure(lookup.Error);
        }

        var client = lookup.Value;

        var secret = ClientSecrets.Generate();
        if (!await _clientProvider.UpdateSecretAsync(client.Id, workspaceId, ClientSecrets.Hash(secret), cancellationToken))
        {
            return Result<ClientSecretResponse>.Failure(WorkspaceOwnership.ClientNotFound);
        }

        _logger.LogInformation("Rotated secret for client ID {ClientId}", client.Id);
        await _access.SecretRotatedAsync(client, cancellationToken);

        return Result<ClientSecretResponse>.Success(new ClientSecretResponse(client.RefId, secret));
    }

    private Task<Result<ClientDetail>> FindInWorkspaceAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        return WorkspaceOwnership.LoadAsync(workspaceId, () => _clientProvider.FindDetailByRefIdAsync(clientRefId, cancellationToken), WorkspaceOwnership.ClientNotFound);
    }

    public async Task<Result> RenameAsync(int workspaceId, Guid clientRefId, string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Renaming client RefId {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        var lookup = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (lookup.IsFailure)
        {
            return Result.Failure(lookup.Error);
        }

        var client = lookup.Value;
        var id = client.Id;

        var oldName = client.Name;
        if (oldName == name)
        {
            _logger.LogDebug("Client ID {ClientId} is already named '{Name}'. Skipping update.", id, name);
            return Result.Success();
        }

        await _clientProvider.UpdateNameAsync(id, name, cancellationToken);
        _logger.LogInformation("Successfully renamed client ID {ClientId} from '{OldName}' to '{NewName}'", id, oldName, name);

        await _eventBus.PublishAsync(new ClientRenamedEvent(client.RefId, client.Id, client.WorkspaceId, oldName, name)
        {
            Changes = [new FieldChange("name", oldName, name)]
        }, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<ClientTagsResponse>> SetTagsAsync(int workspaceId, Guid clientRefId, IReadOnlyList<string>? tags, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting tags on client RefId {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        if (tags is null)
        {
            return Result<ClientTagsResponse>.Failure(ClientTags.Invalid);
        }

        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in tags)
        {
            if (!ClientTags.TryNormalize(raw, out var tag))
            {
                return Result<ClientTagsResponse>.Failure(ClientTags.Invalid);
            }

            normalized.Add(tag);
        }

        if (normalized.Count > ClientTags.MaxPerClient)
        {
            return Result<ClientTagsResponse>.Failure(Error.Validation("CLIENT_TAGS_TOO_MANY", $"A client can have at most {ClientTags.MaxPerClient} tags."));
        }

        var lookup = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (lookup.IsFailure)
        {
            return Result<ClientTagsResponse>.Failure(lookup.Error);
        }

        var client = lookup.Value;
        if (!await _clientProvider.SetTagsAsync(client.Id, workspaceId, normalized, cancellationToken))
        {
            // Deleted between the lookup and the write.
            return Result<ClientTagsResponse>.Failure(WorkspaceOwnership.ClientNotFound);
        }

        _logger.LogInformation("Set {Count} tags on client ID {ClientId}", normalized.Count, client.Id);
        return Result<ClientTagsResponse>.Success(new ClientTagsResponse(client.RefId, normalized.ToList()));
    }

    // A status the protocol does not define would lock the client out and show it as offline.
    private static readonly Error StatusInvalid = Error.Validation("CLIENT_STATUS_INVALID", "Status must be Pending, Registered, Revoked or Rejected.");

    private static readonly Error PolicyFull = Error.Validation("POLICY_LIMIT_REACHED", "Policy registration limit reached. Cannot approve more clients.");
}
