namespace Wbskt.Events;

[AttributeUsage(AttributeTargets.Class)]
public class SignalRNotifyAttribute : Attribute
{
    public string ClientMethod { get; }

    public SignalRNotifyAttribute(string clientMethod)
    {
        ClientMethod = clientMethod;
    }
}
