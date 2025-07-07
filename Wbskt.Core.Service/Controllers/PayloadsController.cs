using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PayloadsController(ILogger<PayloadsController> logger, IPayloadDispatcher payloadDispatcher) : ControllerBase
{
    [HttpGet("{publisherId:guid}/dispatch")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public async Task<IActionResult> Dispatch(Guid publisherId)
    {
        var payload = new ClientPayload
        {
            PublisherRef = publisherId
        };

        return await DispatchInternal(payload) ? Ok() : BadRequest($"no channels with publisherId: {payload.PublisherRef}");
    }

    [HttpPost("/dispatch")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public async Task<IActionResult> Dispatch(ClientPayload payload)
    {
        return await DispatchInternal(payload) ? Ok() : BadRequest($"no channels with publisherId: {payload.PublisherRef}");
    }

    private async Task<bool> DispatchInternal(ClientPayload payload)
    {
        payload.PayloadId = Guid.NewGuid();
        logger.LogDebug("payloadRef is: {ref}", payload.PayloadId);
        var payloadSend = await payloadDispatcher.DispatchPayload(payload);
        return payloadSend;
    }
}
