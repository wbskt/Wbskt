using MassTransit;
using Wbskt.Events.Billing;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// Records a tenant's credit grant in the main database, where <c>dbo.Run_Start</c> counts it. Recorded
/// once per source and reference, so a redelivered event adds nothing.
/// </summary>
public sealed class TenantCreditsGrantedHandler(ICreditGrantProvider grants, ILogger<TenantCreditsGrantedHandler> logger) : IConsumer<TenantCreditsGrantedEvent>
{
    public async Task Consume(ConsumeContext<TenantCreditsGrantedEvent> context)
    {
        TenantCreditsGrantedEvent granted = context.Message;
        bool recorded = await grants.InsertAsync(granted.TenantId, granted.Credits, granted.CreatedAtUtc, granted.ExpiresAt.UtcDateTime, granted.Source, granted.SourceRef, context.CancellationToken);
        if (recorded)
        {
            logger.LogInformation("Granted {Credits} credits to tenant {TenantId} until {ExpiresAt} ({Source} {SourceRef}).",
                granted.Credits, granted.TenantId, granted.ExpiresAt, granted.Source, granted.SourceRef);
        }
    }
}

/// <summary>Revokes a tenant's credit grant, as a refund does. Revoking twice changes nothing.</summary>
public sealed class TenantCreditGrantRevokedHandler(ICreditGrantProvider grants, TimeProvider clock, ILogger<TenantCreditGrantRevokedHandler> logger)
    : IConsumer<TenantCreditGrantRevokedEvent>
{
    public async Task Consume(ConsumeContext<TenantCreditGrantRevokedEvent> context)
    {
        TenantCreditGrantRevokedEvent revoked = context.Message;
        if (await grants.RevokeAsync(revoked.Source, revoked.SourceRef, clock.GetUtcNow().UtcDateTime, context.CancellationToken))
        {
            logger.LogInformation("Revoked the credit grant {Source} {SourceRef} of tenant {TenantId}.", revoked.Source, revoked.SourceRef, revoked.TenantId);
        }
    }
}
