using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ClientsController(Logger<ClientsController> logger, IClientsManagementService clientsService, IEnrollmentService enrollmentService) : ControllerBase
{
    [HttpGet]
    public IActionResult GetClients()
    {
        logger.LogTrace("getting all clients");
        var userId = User.GetUserId();
        var clients = clientsService.GetAllByUserId(userId);
        return Ok(clients);
    }

    [HttpPost("{policyRef:guid}/enroll")]
    public IActionResult Enroll(Guid policyRef, [FromBody] ClientRecord clientRecord)
    {
        logger.LogTrace("enrollment endpoint called with policyRef {policyRef}", policyRef);
        
        try
        {
            // Validate the enrollment policyRef
            if (!enrollmentService.ValidateEnrollmentCode(policyRef.ToString(), out var policyUserId))
            {
                return BadRequest(new { error = "Invalid or expired enrollment policy" });
            }

            // Set the user ID from the policy for the client record
            var clientRecordWithUserId = clientRecord with { UserId = policyUserId };
            
            // Register the client
            var clientId = clientsService.UpsertClient(clientRecordWithUserId);
            
            logger.LogInformation("client registered successfully with id {clientId} using enrollment policyRef {policyRef}", clientId, policyRef);
            
            return Ok(new { 
                message = "Enrollment successful", 
                clientId = clientId,
                policyUserId = policyUserId 
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "error during enrollment with policyRef {policyRef}", policyRef);
            return StatusCode(500, new { error = "An error occurred during enrollment" });
        }
    }

    [HttpPost("connect")]
    public IActionResult RequestConnection()
    {
        logger.LogTrace("connection request endpoint called");
        return Ok();
    }
}
