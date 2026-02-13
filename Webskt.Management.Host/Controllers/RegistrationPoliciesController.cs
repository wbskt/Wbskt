using Webskt.Common.Abstraction.Models.Management;
using Microsoft.AspNetCore.Mvc;
using Webskt.Common.Abstraction.Models;
using Webskt.Management.Host.Services;

namespace Webskt.Management.Host.Controllers;

[Route("api/registration-policies")]
[ApiController]
public class RegistrationPoliciesController : ControllerBase
{
    private readonly IRegistrationPolicyService _policyService;

    public RegistrationPoliciesController(IRegistrationPolicyService policyService)
    {
        _policyService = policyService;
    }

    [HttpGet]
    public async Task<ListResponse<RegistrationPolicyResponse>> GetAll(
        [FromQuery] bool? autoApproval,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var pagedData = await _policyService.GetAllAsync(autoApproval, name, skip, take, cancellationToken);

        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());

        return new ListResponse<RegistrationPolicyResponse>
        {
            Items = pagedData
        };
    }

    [HttpGet("{refId:guid}")]
    public async Task<RegistrationPolicyResponse> Get(Guid refId, CancellationToken cancellationToken)
    {
        return await _policyService.GetByRefIdAsync(refId, cancellationToken);
    }

    [HttpPost]
    public async Task<RegistrationPolicyResponse> Create(RegistrationPolicyRequest request, CancellationToken cancellationToken)
    {
        return await _policyService.CreateAsync(request, cancellationToken);
    }
}
