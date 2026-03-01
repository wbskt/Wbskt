namespace Webskt.Workflow.Engine.Host.Models.TriggerContexts;

public abstract class BaseTriggerContext
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
