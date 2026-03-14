namespace Wbskt.Common.Abstraction;

[AttributeUsage(AttributeTargets.Class)]
public class SignalRNotifyAttribute : Attribute
{
    public string ClientMethod { get; }
    public NotificationScope Scope { get; }

    public SignalRNotifyAttribute(string clientMethod, NotificationScope scope = NotificationScope.Workspace)
    {
        ClientMethod = clientMethod;
        Scope = scope;
    }
}

public enum NotificationScope
{
    Session,
    User,
    Workspace,
    Tenant,
}
