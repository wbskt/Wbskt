namespace Webskt.Common.Abstraction.Exceptions;

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
