using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// Announces each credit grant that runs out within <see cref="Warning"/> with credits still undrawn
/// (<see cref="TenantCreditGrantExpiringEvent"/>), once: the grant is marked as it is claimed, and given
/// back if the event cannot be published. Here rather than in the auth host because only the main
/// database knows what a grant has left.
/// </summary>
public sealed class CreditGrantExpiryWarning(ICreditGrantProvider grants, IEventBus eventBus, TimeProvider clock, ILogger<CreditGrantExpiryWarning> logger)
{
    internal static readonly TimeSpan Warning = TimeSpan.FromDays(30);

    public async Task WarnAsync(CancellationToken ct)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        foreach (ExpiringCreditGrant grant in await grants.ClaimExpiringAsync(now, now + Warning, now, ct))
        {
            try
            {
                await eventBus.PublishAsync(
                    new TenantCreditGrantExpiringEvent(grant.TenantId, grant.Source, grant.SourceRef, grant.Remaining, new DateTimeOffset(grant.ExpiresAt, TimeSpan.Zero))
                    {
                        WorkspaceIds = grant.WorkspaceIds
                    },
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not announce that credit grant {Source} {SourceRef} of tenant {TenantId} expires soon; trying again next run.", grant.Source, grant.SourceRef, grant.TenantId);
                await grants.ReleaseExpiryWarningAsync(grant.Source, grant.SourceRef, CancellationToken.None);
            }
        }
    }
}
