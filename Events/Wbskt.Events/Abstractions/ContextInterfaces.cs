using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Abstractions;

public interface IWorkspaceContext : IEvent
{
    [SignalRPrivate] int WorkspaceId { get; }
}

public interface IPolicyContext : IWorkspaceContext
{
    [SignalRPrivate] int PolicyId { get; }
    Guid PolicyRefId { get; }
}

public interface IClientContext : IWorkspaceContext
{
    [SignalRPrivate] int ClientId { get; }
    Guid ClientRefId { get; }
}

public interface IWorkflowContext : IWorkspaceContext
{
    [SignalRPrivate] int WorkflowId { get; }
    Guid WorkflowRefId { get; }
}

public interface IUserContext
{
    [SignalRPrivate] int UserId { get; }
    Guid UserRefId { get; }
}
