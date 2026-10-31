namespace Wbskt.Events;

[AttributeUsage(AttributeTargets.Class)]
public class SignalRNotifyAttribute : Attribute
{
    public string ClientMethod { get; }

    public NotifyScope Scope { get; set; } = NotifyScope.Workspace;

    public SignalRNotifyAttribute(string clientMethod)
    {
        ClientMethod = clientMethod;
    }
}
