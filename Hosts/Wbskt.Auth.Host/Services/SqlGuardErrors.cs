using System.Security;
using Microsoft.Data.SqlClient;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// The exceptions from the auth database that are outcomes rather than faults, and the error each one
/// reads back as. Anything else is a fault and propagates to <c>GlobalExceptionMiddleware</c>.
/// </summary>
internal static class SqlGuardErrors
{
    public static readonly Error InvalidInvitation =
        Error.Validation("INVITATION_INVALID", "This invitation is not valid. It may have expired, been revoked, already been used, or been sent to a different email address.");

    /// <summary>
    /// Translates a guard. <paramref name="conflictError"/> is what a unique-key violation means for
    /// this operation ("that name is taken"); without one, a duplicate is a fault.
    /// </summary>
    public static bool TryTranslate(Exception ex, Error? conflictError, out Error error)
    {
        error = ex switch
        {
            SqlException { Number: 2601 or 2627 } when conflictError is not null => conflictError,

            // A record the caller named does not exist (or is not theirs to see).
            SecurityException => Error.NotFound("AUTH_NOT_FOUND", "The requested record was not found."),

            // Redemption lost a race, or the token stopped being valid between the read-back and the
            // procedure. Reported identically to the pre-check path, so which of the two rejected it
            // is not observable from outside.
            SqlException { Number: 50011 } => InvalidInvitation,

            // A workspace in another tenant reads exactly like one that does not exist, so an
            // invitation cannot be used to learn which workspace references are real elsewhere.
            SqlException { Number: 50017 } => Error.Forbidden("WORKSPACE_NOT_FOUND", "Workspace not found."),

            SqlException { Number: 50012 } => Error.Conflict("AUTH_ALREADY_MEMBER", "That user is already a member of this tenant."),

            // THROW 50001-50019 from the stored procedures are deliberate guards (wrong tenant, group
            // has children, cannot remove the owner or the last administrator, unknown permission
            // slug, expired invitation) rather than faults, so they read back as validation. The
            // upper bound is deliberately ahead of the codes in use: a new guard that lands outside
            // this range reaches the caller as a 500, which is the opposite of what a guard is for.
            SqlException { Number: >= 50001 and <= 50019 } guard => Error.Validation("AUTH_OPERATION_REJECTED", guard.Message),

            _ => null!
        };

        return error is not null;
    }
}
