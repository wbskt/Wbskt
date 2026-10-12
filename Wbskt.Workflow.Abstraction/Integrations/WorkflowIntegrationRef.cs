namespace Wbskt.Workflow.Abstraction.Integrations;

/// <summary>A step of a definition being published that sends through an integration, by the integration's RefId.</summary>
public sealed record WorkflowIntegrationRef(Guid NodeId, string NodeName, Guid IntegrationRefId)
{
    /// <summary>dbo.WorkflowIntegrationRefs.NodeName's length.</summary>
    public const int MaxNodeNameLength = 200;
}
