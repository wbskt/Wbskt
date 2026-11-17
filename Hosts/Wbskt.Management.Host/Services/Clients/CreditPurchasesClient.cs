using System.Globalization;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>
/// <c>GET /api/internal/billing/credit-purchases</c> on the auth host. Called by a background job, with no
/// user token to forward, so it carries the shared internal key (<see cref="AuthInternalApiKeyHandler"/>).
/// </summary>
internal sealed class CreditPurchasesClient(HttpClient httpClient) : ICreditPurchasesClient
{
    public async Task<Page<CreditPurchaseResponse>> GetPageAsync(string? cursor, DateTimeOffset expiresAfter, CancellationToken cancellationToken)
    {
        string query = $"limit={Paging.MaxPageSize}&expiresAfter={Uri.EscapeDataString(expiresAfter.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}"
            + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
        using var response = await httpClient.GetAsync($"/{InternalRoutes.CreditPurchases}?{query}", cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Page<CreditPurchaseResponse>>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The auth host answered the credit purchases request with an empty body.");
    }
}
