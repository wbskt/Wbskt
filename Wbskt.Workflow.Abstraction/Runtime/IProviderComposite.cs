using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IProviderComposite
{
    IWorkflowDefinitionProvider WorkflowDefinition { get; }

    ITriggerRegistrationProvider TriggerRegistration { get; }

    IBookmarkProvider Bookmark { get; }

    ISharedVariableProvider SharedVariable { get; }

    IIdempotencyKeyProvider IdempotencyKey { get; }

    IPendingTriggerEventProvider PendingTriggerEvent { get; }

    IScheduledFireProvider ScheduledFire { get; }
}
