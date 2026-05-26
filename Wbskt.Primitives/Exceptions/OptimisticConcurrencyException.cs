namespace Wbskt.Primitives.Exceptions;

public class OptimisticConcurrencyException : Exception
{
    public OptimisticConcurrencyException(string message) : base(message)
    {
    }
}
