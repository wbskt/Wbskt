namespace Wbskt.Common.Configurations;

public class SendEmailConfiguration : StepConfigurationBase
{
    public required string IntegrationName { get; set; }
    public required string To { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
}