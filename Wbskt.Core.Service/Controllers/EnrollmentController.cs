using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class EnrollmentController(IEnrollmentService enrollmentService) : ControllerBase
{
    [HttpPost]
    public EnrollmentPolicyRecord CreateEnrollmentCode([FromBody] EnrollmentPolicyRecord record)
    {
        var userId = User.GetUserId();
        record.UserId = userId;
        enrollmentService.CreatePolicy(record);
        return record;
    }

    [HttpGet]
    public IEnumerable<EnrollmentPolicyRecord> GetAllForUser()
    {
        var userId = User.GetUserId();
        var policies = enrollmentService.GetPoliciesByUserId(userId);
        return policies;
    }
}
