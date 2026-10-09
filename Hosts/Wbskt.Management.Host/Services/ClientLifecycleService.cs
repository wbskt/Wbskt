using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Changes to a client: its status, name and tags, its secret, and deleting it. Each change that
/// affects a connected device also tells the socket host, through <see cref="ClientAccessRevoker"/>.
/// </summary>
internal sealed class ClientLifecycleService : IClientLifecycleService
{
    private readonly IClientProvider _clientProvider;
    private readonly IEventBus _eventBus;
    private readonly ClientAccessRevoker _access;
    private readonly ILogger<ClientLifecycleService> _logger;

    public ClientLifecycleService(
        IClientProvider clientProvider,
        IEventBus eventBus,
        IClientTokenCutoffs cutoffs,
        ILogger<ClientLifecycleService> logger)
    {
        _clientProvider = clientProvider;
        _eventBus = eventBus;
        _access = new ClientAccessRevoker(eventBus, cutoffs);
        _logger = logger;
    }

    public async Task<Result> UpdateStatusAsync(int workspaceId, Guid clientRefId, ClientStatus status, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating client RefId {ClientRefId} status to '{ClientStatus}' in WorkspaceId: {WorkspaceId}", clientRefId, status, workspaceId);

        var change = (await _clientProvider.UpdateStatusesAsync(workspaceId, [clientRefId], status, cancellationToken)).Single();
        switch (change.Outcome)
        {
            case ClientStatusOutcome.NotFound or ClientStatusOutcome.OtherWorkspace:
                _logger.LogWarning("Failed to update client status: RefId {ClientRefId} not found in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);
                return Result.Failure(WorkspaceOwnership.ClientNotFound);
            case ClientStatusOutcome.PolicyLimitReached:
                _logger.LogWarning("Client approval failed: Policy registration limit reached for Policy ID {PolicyId}", change.PolicyId);
                return Result.Failure(PolicyFull);
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

        // One database call for the whole list, in the order given: approvals compete for the
        // policy's remaining places, and a predictable order decides which ones get them.
        IReadOnlyList<ClientStatusChange> changes;
        changes = await _clientProvider.UpdateStatusesAsync(workspaceId, clientRefIds.Distinct().ToList(), status, cancellationToken);

        var updated = new List<Guid>();
        var failed = new List<BulkClientStatusFailure>();
        foreach (var change in changes)
        {
            Error? error = change.Outcome switch
            {
                // A missing client and another workspace's read the same; see WorkspaceOwnership.
                ClientStatusOutcome.NotFound or ClientStatusOutcome.OtherWorkspace => WorkspaceOwnership.ClientNotFound,
                ClientStatusOutcome.PolicyLimitReached => PolicyFull,
                _ => null
            };

            if (error is null && change.Outcome == ClientStatusOutcome.Updated)
            {
                try
                {
                    await AnnounceStatusChangeAsync(workspaceId, change, status, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Client RefId {ClientRefId} changed status but announcing it failed. Error: {Message}", change.RefId, ex.Message);
                    _logger.LogTrace(ex, "AnnounceStatusChangeAsync exception stack trace for ClientRefId {ClientRefId}", change.RefId);
                    error = Error.Failure("CLIENT_UPDATE_ERROR", ex.Message);
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

        await _eventBus.PublishAsync(new ClientRenamedEvent(client.RefId, client.Id, client.WorkspaceId, oldName, name), cancellationToken);

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

    private static readonly Error PolicyFull = Error.Validation("POLICY_LIMIT_REACHED", "Policy registration limit reached. Cannot approve more clients.");
}
