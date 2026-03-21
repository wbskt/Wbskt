namespace Wbskt.Events.Abstractions;

public interface IClientContext : IWorkspaceContext
{
    [SignalRPrivate] int ClientId { get; }
    Guid ClientRefId { get; }
}