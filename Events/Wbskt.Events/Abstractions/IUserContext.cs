namespace Wbskt.Events.Abstractions;

public interface IUserContext
{
    [SignalRPrivate] int UserId { get; }
    Guid UserRefId { get; }
}