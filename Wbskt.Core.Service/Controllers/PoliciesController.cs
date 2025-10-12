using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common;
using Wbskt.Core.Service.Contracts;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policyService;

    public PoliciesController(IPolicyService policyService)
    {
        _policyService = policyService;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePolicy(CreatePolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await _policyService.CreatePolicyAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetPolicy), new { refId = policy.RefId }, policy);
    }

    [HttpGet]
    public async Task<IActionResult> GetPolicies(CancellationToken cancellationToken)
    {
        var policies = await _policyService.GetPoliciesAsync(cancellationToken);
        return Ok(policies);
    }

    [HttpGet("{refId}")]
    public async Task<IActionResult> GetPolicy(Guid refId, CancellationToken cancellationToken)
    {
        var policy = await _policyService.GetPolicyAsync(refId, cancellationToken);

        if (policy == null)
        {
            return NotFound();
        }

        return Ok(policy);
    }

    [HttpPut("{refId}")]
    public async Task<IActionResult> UpdatePolicy(Guid refId, UpdatePolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await _policyService.UpdatePolicyAsync(refId, request, cancellationToken);
        return Ok(policy);
    }

    [HttpDelete("{refId}")]
    public async Task<IActionResult> DeletePolicy(Guid refId, CancellationToken cancellationToken)
    {
        await _policyService.DeletePolicyAsync(refId, cancellationToken);
        return NoContent();
    }
}
