using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Infrastructure.Csv;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Readings;
using Wbskt.Management.Host.RateLimiting;
using Wbskt.Management.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

/// <summary>
/// The history of a client's numeric state variables: every number it reported in a state report,
/// for charting (summarised per bucket of time) or for export as CSV.
/// </summary>
[Route("api/v1/workspaces/{workspaceRef:guid}/clients/{clientRef:guid}/readings")]
[ApiController]
[Authorize]
[RequiresPermission(PermissionNames.ClientsRead)]
public class ClientReadingsController : ApiControllerBase
{
    private readonly IClientReadingService _readingService;

    public ClientReadingsController(IClientReadingService readingService)
    {
        _readingService = readingService;
    }

    /// <summary>
    /// One state variable's readings over a time range, as count, min, average and max per bucket.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRef">The unique reference ID of the client.</param>
    /// <param name="name">The state variable, as the client names it in its state reports.</param>
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
        return MapResult(await _readingService.GetBucketsAsync(workspaceId, clientRef, name, from, to, bucket, cancellationToken));
    }

    /// <summary>
    /// The readings over a time range as CSV, for one state variable or all of them: every reading
    /// (name, sentAt, receivedAt, value, late), which reaches back only the plan's raw days, or with
    /// <c>resolution=hourly</c> one row per variable and hour (name, hourStart, count, min, avg, max)
    /// as far back as the chart.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRef">The unique reference ID of the client.</param>
    /// <param name="name">One state variable; every variable when left out.</param>
    /// <param name="from">Start of the range (inclusive). Defaults to a day before <paramref name="to"/>.</param>
    /// <param name="to">End of the range (exclusive). Defaults to now.</param>
    /// <param name="resolution"><c>raw</c> (the default) or <c>hourly</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A CSV file, oldest row first.</returns>
    [HttpGet("csv")]
    [WorkspaceRateLimit(RateLimitPolicies.Export)]
    [Produces("text/csv")]
    public async Task<IActionResult> GetCsv(
        [FromWorkspace] int workspaceId,
        Guid clientRef,
        [FromQuery] string? name,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? resolution,
        CancellationToken cancellationToken = default)
    {
        var csv = await _readingService.ExportCsvAsync(workspaceId, clientRef, name, from, to, resolution, cancellationToken);
        if (csv.IsFailure)
        {
            return MapError(csv.Error);
        }

        return new CsvFileResult($"readings-{clientRef}.csv", csv.Value);
    }
}
