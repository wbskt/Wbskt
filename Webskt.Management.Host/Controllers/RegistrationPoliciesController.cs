using Webskt.Common.Abstraction.Models.Management;
using Microsoft.AspNetCore.Mvc;
using Webskt.Management.Host.Models;
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
    public async Task<IReadOnlyCollection<RegistrationPolicyResponse>> GetAll()
    {
        return await _policyService.GetAllAsync();
    }

    [HttpGet("{refId:guid}")]
    public async Task<RegistrationPolicyResponse> Get(Guid refId)
    {
        return await _policyService.GetByRefIdAsync(refId);
    }

    [HttpPost]
    public async Task<RegistrationPolicyResponse> Create(RegistrationPolicyRequest request)
    {
        return await _policyService.CreateAsync(request);
    }
}
