using System.Runtime.ExceptionServices;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// What a sign-in fault propagates as. GlobalExceptionMiddleware logs a fault's message and puts it
/// in the <c>SystemErrorEvent</c> tenant admins can read, and a fault here can quote the address
/// being signed in or SQL detail. This carries only the original's type name and its stack frames,
/// and no inner exception, so neither the log line nor the event can repeat the original's text.
/// It is unmapped, so the caller gets the same generic 500 the original would have produced.
/// </summary>
internal sealed class SignInFaultException : Exception
{
    private SignInFaultException(string message) : base(message)
    {
    }

    /// <summary>
    /// A stand-in for <paramref name="fault"/> whose stack trace begins with the original's frames,
    /// so the log still shows where the fault happened. Must be thrown, not stored: the frames are
    /// attached as a remote stack trace, which only an unthrown exception accepts.
    /// </summary>
    public static SignInFaultException From(Exception fault)
    {
        var sanitized = new SignInFaultException($"Unexpected {fault.GetType().Name} during sign-in.");
        if (fault.StackTrace is { } frames)
        {
            ExceptionDispatchInfo.SetRemoteStackTrace(sanitized, frames);
        }

        return sanitized;
    }
}
