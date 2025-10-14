using Wbskt.Common.Configurations;
using Wbskt.Common.Services;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Actions;

public class SendEmailAction : IAction
{
    private readonly ILogger<SendEmailAction> _logger;
    private readonly ICredentialService _credentialService;

    public SendEmailAction(ILogger<SendEmailAction> logger, ICredentialService credentialService)
    {
        _logger = logger;
        _credentialService = credentialService;
    }

    public async Task<ActionResult> ExecuteAsync(StepConfigurationBase configuration, WorkflowContext context, CancellationToken cancellationToken)
    {
        var config = configuration as SendEmailConfiguration;
        _logger.LogInformation("Executing SendEmailAction.");

        // 1. Get user ID from context (would need to be passed in)
        // 2. Fetch credentials using _credentialService.GetCredentialsAsync(...)
        // 3. Render templates in subject and body
        // 4. Use SendGrid/Mailgun SDK to send email

        // Placeholder logic:
        var apiKey = await _credentialService.GetCredentialsAsync(0, config.IntegrationName, cancellationToken);
        _logger.LogInformation("Using API Key (placeholder): {ApiKey}", apiKey);
        _logger.LogInformation("Email sent (placeholder) to {Recipient}.", config.To);

        return new ActionResult(true);
    }
}
