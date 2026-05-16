namespace Wbskt.Workflow.Engine.Host.V2;

public interface IWorkflowRuntime : IAsyncDisposable
{
    // an event which will be fired when disposed is called.
    event Action OnDisposed;

    Task ExecuteTriggerAsync(Guid nodeRef, object trigger);
}