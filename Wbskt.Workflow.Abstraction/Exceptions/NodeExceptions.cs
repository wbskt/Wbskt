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

    /// <summary>
    /// Error code surfaced on the resulting <c>NodeExecutionResult.Fail</c>. Subclasses override it so
    /// a run's history names the actual problem instead of the catch-all "PERMANENT_ERROR".
    /// </summary>
    public virtual string ErrorCode => "PERMANENT_ERROR";
}

/// <summary>
/// A workflow expression could not be evaluated: an unsupported expression kind, operand types the
/// operator does not accept, or a function called with the wrong arguments. Permanent - the same
/// definition will fail the same way next time, so retrying is pointless.
/// </summary>
public class ExpressionEvaluationException : PermanentNodeException
{
    public ExpressionEvaluationException(string message) : base(message)
    {
    }

    public override string ErrorCode => "EXPRESSION_EVALUATION_ERROR";
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

/// <summary>
/// A node's <see cref="Models.Nodes.BaseNode.Kind"/> has no registered executor. Permanent by
/// definition - retrying cannot register one - so the branch fails immediately rather than looping.
/// The validator should reject these at publish time; this exception is the runtime backstop for a
/// definition that was published before the kind was gated.
/// </summary>
public class NodeKindNotSupportedException : PermanentNodeException
{
    public NodeKindNotSupportedException(string kind)
        : base($"No executor is registered for node kind '{kind}'.")
    {
        Kind = kind;
    }

    public string Kind { get; }
}
