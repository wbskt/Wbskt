using Wbskt.Auth.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>The auth host's credit packs bought (<c>InternalRoutes.CreditPurchases</c>).</summary>
public interface ICreditPurchasesClient
{
    /// <summary>
    /// One page, after <paramref name="cursor"/>, of the credit packs bought that expire after
    /// <paramref name="expiresAfter"/>. Throws when the auth host cannot answer.
    /// </summary>
    Task<Page<CreditPurchaseResponse>> GetPageAsync(string? cursor, DateTimeOffset expiresAfter, CancellationToken cancellationToken);
}
