namespace Webskt.Foundation.Abstraction.Exceptions;

public class InternalServerException : Exception
{
    public InternalServerException(string message) : base(message) { }
}