namespace Wbskt.Workflow.Abstraction.Exceptions;

public class TransientNodeException : Exception
{
    public TransientNodeException(string message) : base(message)
    {
    }

    public TransientNodeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class PermanentNodeException : Exception
{
    public PermanentNodeException(string message) : base(message)
    {
    }

    public PermanentNodeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class EngineFaultException : Exception
{
    public EngineFaultException(string message) : base(message)
    {
    }

    public EngineFaultException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
