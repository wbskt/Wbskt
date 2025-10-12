using System.Security.Claims;
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
        var userId = GetUserId();
        var policy = await _policyService.CreatePolicyAsync(userId, request, cancellationToken);
        return CreatedAtAction(nameof(GetPolicy), new { id = policy.Id }, policy);
    }

    [HttpGet]
    public async Task<IActionResult> GetPolicies(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var policies = await _policyService.GetPoliciesAsync(userId, cancellationToken);
        return Ok(policies);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPolicy(int id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var policy = await _policyService.GetPolicyAsync(userId, id, cancellationToken);

        if (policy == null)
        {
            return NotFound();
        }

        return Ok(policy);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePolicy(int id, UpdatePolicyRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var policy = await _policyService.UpdatePolicyAsync(userId, id, request, cancellationToken);
        return Ok(policy);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePolicy(int id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        await _policyService.DeletePolicyAsync(userId, id, cancellationToken);
        return NoContent();
    }

    private int GetUserId()
    {
        var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == Constants.Claims.UserData);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token.");
        }
        return userId;
    }
}
