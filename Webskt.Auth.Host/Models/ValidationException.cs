namespace Webskt.Auth.Host.Models;

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
