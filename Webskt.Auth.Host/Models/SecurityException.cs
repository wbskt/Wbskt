namespace Webskt.Auth.Host.Models;

public class SecurityException : Exception
{
    public SecurityException(string message) : base(message) { }
}
