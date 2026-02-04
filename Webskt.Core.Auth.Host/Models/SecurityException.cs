namespace Webskt.Core.Auth.Host.Models;

public class SecurityException : Exception
{
    public SecurityException(string message) : base(message) { }
}
