using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/event-logs")]
[ApiController]
[Authorize]
public sealed class EventLogsController : ControllerBase
{
    private readonly IEventLogService _eventLogService;
    private readonly IAuthServiceClient _authClient;

    public EventLogsController(IEventLogService eventLogService, IAuthServiceClient authClient)
    {
        _eventLogService = eventLogService;
        _authClient = authClient;
    }

    [HttpGet]
    public async Task<ListResponse<EventLogResponse>> GetLogs(
        Guid workspaceRef,
        [FromQuery] string? eventName,
        [FromQuery] EventCriticality? criticality,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "logs.read", cancellationToken);
        
        var pagedList = await _eventLogService.GetLogsAsync(workspaceId, eventName, criticality, skip, take, cancellationToken);
        
        Response.Headers.Append("X-Total-Count", pagedList.TotalCount.ToString());

        return new ListResponse<EventLogResponse>()
        {
            Items = pagedList
        };
    }
}
