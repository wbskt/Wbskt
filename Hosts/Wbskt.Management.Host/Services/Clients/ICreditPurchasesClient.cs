using Wbskt.Auth.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>The auth host's credit packs bought (<c>InternalRoutes.CreditPurchases</c>).</summary>
public interface ICreditPurchasesClient
{
    /// <summary>One page, after <paramref name="cursor"/>, of every credit pack bought. Throws when the auth host cannot answer.</summary>
    Task<Page<CreditPurchaseResponse>> GetPageAsync(string? cursor, CancellationToken cancellationToken);
}
