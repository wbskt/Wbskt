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
    public EnrollmentPolicyRecord CreateEnrollmentCode([FromBody] EnrollmentPolicyRecord record)
    {
        var userId = User.GetUserId();
        record.UserId = userId;
        enrollmentService.CreatePolicy(record);
        return record;
    }

    [HttpGet("policies")]
    public IEnumerable<EnrollmentPolicyRecord> GetPolicies()
    {
        var userId = User.GetUserId();
        var policies = enrollmentService.GetPoliciesByUserId(userId);
        return policies;
    }
}
