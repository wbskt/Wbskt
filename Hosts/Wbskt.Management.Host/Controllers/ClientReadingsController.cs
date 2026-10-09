using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Readings;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

/// <summary>
/// The history of a client's numeric state variables: every number it reported in a state report,
/// for charting (summarised per bucket of time) or for export as CSV.
/// </summary>
[Route("api/workspaces/{workspaceRef:guid}/clients/{clientRef:guid}/readings")]
[ApiController]
[Authorize]
[RequiresPermission(PermissionNames.ClientsRead)]
public class ClientReadingsController : ApiControllerBase
{
    private readonly IClientService _clientService;
    private readonly IClientReadingService _readingService;

    public ClientReadingsController(IClientService clientService, IClientReadingService readingService)
    {
        _clientService = clientService;
        _readingService = readingService;
    }

    /// <summary>
    /// One state variable's readings over a time range, as count, min, average and max per bucket.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRef">The unique reference ID of the client.</param>
    /// <param name="name">The state variable, as the device names it in its state reports.</param>
    /// <param name="from">Start of the range (inclusive). Defaults to a day before <paramref name="to"/>.</param>
    /// <param name="to">End of the range (exclusive). Defaults to now.</param>
    /// <param name="bucket">Bucket size: seconds, or a number with s, m, h or d (e.g. 5m). Picked from the range when left out.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The buckets that hold readings, oldest first.</returns>
    [HttpGet]
    public async Task<ActionResult<ClientReadingsResponse>> Get(
        [FromWorkspace] int workspaceId,
        Guid clientRef,
        [FromQuery] string? name,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? bucket,
        CancellationToken cancellationToken = default)
    {
        var clientId = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRef, cancellationToken);
        if (clientId.IsFailure)
        {
            return MapError(clientId.Error);
        }

        return MapResult(await _readingService.GetBucketsAsync(clientId.Value, name, from, to, bucket, cancellationToken));
    }

    /// <summary>
    /// Every reading over a time range as CSV (name, deviceTime, receivedAt, value, late), for one
    /// state variable or all of them.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRef">The unique reference ID of the client.</param>
    /// <param name="name">One state variable; every variable when left out.</param>
    /// <param name="from">Start of the range (inclusive). Defaults to a day before <paramref name="to"/>.</param>
    /// <param name="to">End of the range (exclusive). Defaults to now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A CSV file, oldest reading first.</returns>
    [HttpGet("csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> GetCsv(
        [FromWorkspace] int workspaceId,
        Guid clientRef,
        [FromQuery] string? name,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var clientId = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRef, cancellationToken);
        if (clientId.IsFailure)
        {
            return MapError(clientId.Error);
        }

        var readings = await _readingService.GetRangeAsync(clientId.Value, name, from, to, cancellationToken);
        if (readings.IsFailure)
        {
            return MapError(readings.Error);
        }

        return File(Encoding.UTF8.GetBytes(ClientReadingsCsv.Write(readings.Value)), "text/csv; charset=utf-8", $"readings-{clientRef}.csv");
    }
}
