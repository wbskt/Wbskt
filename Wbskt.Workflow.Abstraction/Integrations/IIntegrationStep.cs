namespace Wbskt.Workflow.Abstraction.Integrations;

/// <summary>A step whose config can name an integration to send through.</summary>
public interface IIntegrationStep
{
    /// <summary>The integration's RefId, or null when the step names none (an empty GUID names none too).</summary>
    Guid? IntegrationRef { get; }
}
