namespace Webskt.Foundation.Abstraction.Exceptions;

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
