using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Integrations;

namespace Wbskt.Workflow.Engine.Host.Controllers;

/// <summary>
/// Sealing and testing integrations for the management host, which holds no master key. Not an
/// [InboundEndpoint]: nothing here touches runs, so any replica answers, leader or not. The inbound
/// API key is still required, as it is for every endpoint not marked anonymous.
/// </summary>
[ApiController]
public sealed class IntegrationsController(IIntegrationVault vault) : ApiControllerBase
{
    /// <summary>Validates an integration and seals its secret. 400 when it is not valid; 503 when no master key is configured.</summary>
    [HttpPost(EngineIntegrationsApi.SealRoute)]
    public ActionResult<IntegrationSealResponse> Seal([FromBody] IntegrationSealRequest request) =>
        MapResult(vault.Seal(request));

    /// <summary>Tries an integration against its service. A failed test is a 200 with ok false; 400 only when the integration is not valid.</summary>
    [HttpPost(EngineIntegrationsApi.TestRoute)]
    public async Task<ActionResult<IntegrationProbeResponse>> Test([FromBody] IntegrationProbeRequest request, CancellationToken ct) =>
        MapResult(await vault.TestAsync(request, ct));
}
