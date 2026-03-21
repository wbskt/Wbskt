namespace Wbskt.Events.Abstractions;

public interface IPolicyContext : IWorkspaceContext
{
    [SignalRPrivate] int PolicyId { get; }
    Guid PolicyRefId { get; }
}