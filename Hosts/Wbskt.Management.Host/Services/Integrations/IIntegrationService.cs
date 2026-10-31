using Wbskt.Infrastructure;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Integrations;

/// <summary>A workspace's integrations: its accounts with outside services that workflow steps send through.</summary>
public interface IIntegrationService
{
    /// <summary>The workspace's integrations by name, optionally of one type (<c>INTEGRATION_TYPE_INVALID</c> for an unknown one).</summary>
    Task<Result<IReadOnlyList<IntegrationResponse>>> GetAllAsync(int workspaceId, string? type, CancellationToken ct);

    Task<Result<IntegrationResponse>> GetAsync(int workspaceId, Guid refId, CancellationToken ct);

    /// <summary>
    /// Adds one. Validation codes for bad input; <c>INTEGRATION_NAME_TAKEN</c> and
    /// <c>INTEGRATION_LIMIT_REACHED</c> (409); <c>INTEGRATIONS_UNAVAILABLE</c> (503) when the engine,
    /// which seals the secret, cannot be reached.
    /// </summary>
    Task<Result<IntegrationResponse>> CreateAsync(int workspaceId, IntegrationCreateRequest request, CancellationToken ct);

    /// <summary>Renames it, replaces its settings, or patches its secret; <c>INTEGRATION_TYPE_IMMUTABLE</c> for a type change.</summary>
    Task<Result<IntegrationResponse>> UpdateAsync(int workspaceId, Guid refId, IntegrationUpdateRequest request, CancellationToken ct);

    /// <summary>Deletes it, or refuses with <c>INTEGRATION_IN_USE</c> and the published steps that use it.</summary>
    Task<IntegrationDeleteResult> DeleteAsync(int workspaceId, Guid refId, CancellationToken ct);

    Task<Result<IntegrationUsageResponse>> GetUsageAsync(int workspaceId, Guid refId, CancellationToken ct);

    /// <summary>Tries a saved or a draft integration; see <see cref="IntegrationTestRequest"/>.</summary>
    Task<Result<IntegrationTestResponse>> TestAsync(int workspaceId, IntegrationTestRequest request, CancellationToken ct);

    /// <summary>The chats a saved Telegram integration's bot has heard from lately; <c>INTEGRATION_TYPE_UNSUPPORTED</c> for another type.</summary>
    Task<Result<TelegramChatsResponse>> GetTelegramChatsAsync(int workspaceId, Guid refId, CancellationToken ct);
}

/// <summary>A delete's outcome: done, refused with the steps in the way, or another failure.</summary>
public sealed record IntegrationDeleteResult(Result Result, IReadOnlyList<IntegrationUsedBy> UsedBy);
