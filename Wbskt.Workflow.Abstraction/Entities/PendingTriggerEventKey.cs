namespace Wbskt.Workflow.Abstraction.Entities;

public sealed record PendingTriggerEventKey(Guid WorkflowRefId, Guid TriggerNodeId, string CorrelationKey);
