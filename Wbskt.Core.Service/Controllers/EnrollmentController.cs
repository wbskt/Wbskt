using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/enroll")]
[ApiController]
[Authorize]
public class EnrollmentController(IEnrollmentService enrollmentService) : ControllerBase
{
    [HttpPost("code")]
    public EnrollmentPolicyRecord CreateEnrollmentCode([FromBody] EnrollmentPolicyRecord request)
    {
        var userId = User.GetUserId();
        var policyRecord = request with { UserId = userId };
        var response = enrollmentService.CreatePolicy(policyRecord);
        return response;
    }

    [HttpGet("policies")]
    public IEnumerable<EnrollmentPolicyRecord> GetPolicies()
    {
        var userId = User.GetUserId();
        var policies = enrollmentService.GetPoliciesByUserId(userId);
        return policies;
    }
}
