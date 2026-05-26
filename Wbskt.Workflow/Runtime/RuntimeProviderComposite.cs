using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class RuntimeProviderComposite : IProviderComposite
{
    public RuntimeProviderComposite(
        IWorkflowDefinitionProvider workflowDefinition,
        ITriggerRegistrationProvider triggerRegistration,
        IBookmarkProvider bookmark,
        ISharedVariableProvider sharedVariable,
        IIdempotencyKeyProvider idempotencyKey,
        IPendingTriggerEventProvider pendingTriggerEvent,
        IScheduledFireProvider scheduledFire)
    {
        WorkflowDefinition = workflowDefinition;
        TriggerRegistration = triggerRegistration;
        Bookmark = bookmark;
        SharedVariable = sharedVariable;
        IdempotencyKey = idempotencyKey;
        PendingTriggerEvent = pendingTriggerEvent;
        ScheduledFire = scheduledFire;
    }

    public IWorkflowDefinitionProvider WorkflowDefinition { get; }

    public ITriggerRegistrationProvider TriggerRegistration { get; }

    public IBookmarkProvider Bookmark { get; }

    public ISharedVariableProvider SharedVariable { get; }

    public IIdempotencyKeyProvider IdempotencyKey { get; }

    public IPendingTriggerEventProvider PendingTriggerEvent { get; }

    public IScheduledFireProvider ScheduledFire { get; }
}
