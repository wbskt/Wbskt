using Microsoft.AspNetCore.Mvc;

namespace Wbskt.Core.Service.Controllers;

[Route("api/enroll")]
[ApiController]
public class EnrollmentController : ControllerBase
{
    [HttpPost("code")]
    public IActionResult CreateEnrollmentCode()
    {
        return Ok();
    }
}
