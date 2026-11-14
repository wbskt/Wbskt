using System.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Models.Admin;

namespace Wbskt.Infrastructure.StaffActions;

/// <remarks>
/// The change and its audit row share an ambient <see cref="TransactionScope"/>. SqlClient keeps a
/// connection opened inside the scope bound to it, so a provider that opens, uses and closes its
/// connection, followed by another doing the same, runs on that one connection: the transaction never
/// becomes a distributed one, which .NET on Linux cannot run. Only staff actions take this path; the
/// same service methods called from anywhere else run without a scope, exactly as before.
/// <para>
/// The audit row goes in first. Its unique idempotency key makes a second call with the same key wait
/// for the first to commit, then fail on the key, and that failure is answered with the first call's row.
/// </para>
/// </remarks>
public sealed class StaffActionRunner(IStaffAuditProvider audit, ILogger<StaffActionRunner> logger) : IStaffActionRunner
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;
    public const int MaxStaffNameLength = 100;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<Result<StaffActionResponse>> RunAsync(
        StaffChange staff,
        StaffAction action,
        Func<CancellationToken, Task<Result>> change,
        CancellationToken cancellationToken)
    {
        if (Validate(staff) is { } invalid)
        {
            return invalid;
        }

        staff = staff with { Reason = staff.Reason.Trim(), StaffName = staff.StaffName.Trim() };
        if (await audit.FindByIdempotencyKeyAsync(staff.IdempotencyKey, cancellationToken) is { } earlier)
        {
            return Replay(earlier, action);
        }

        Result outcome;
        try
        {
            using var scope = new TransactionScope(
                TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = Timeout },
                TransactionScopeAsyncFlowOption.Enabled);

            long auditId = await audit.InsertAsync(new StaffAuditEntry(staff, action, StaffOutcome.Succeeded, null), cancellationToken);
            outcome = await change(cancellationToken);
            if (outcome.IsSuccess)
            {
                scope.Complete();
                return new StaffActionResponse(auditId, false);
            }
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return await ReplayAfterRaceAsync(staff, action, cancellationToken);
        }

        // The scope rolled the change and its row back; the refusal gets a row of its own.
        try
        {
            await audit.InsertAsync(new StaffAuditEntry(staff, action, StaffOutcome.Refused, outcome.Error.Code), cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            logger.LogInformation("Staff action {Action} was refused ({Code}) while a retry with the same key was recorded.", action.Name, outcome.Error.Code);
        }

        return outcome.Error;
    }

    private static Error? Validate(StaffChange staff)
    {
        if (staff.StaffRefId == Guid.Empty || string.IsNullOrWhiteSpace(staff.StaffName) || staff.StaffName.Trim().Length > MaxStaffNameLength)
        {
            return Error.Validation("STAFF_INVALID", $"Name the staff member: a RefId and a name of at most {MaxStaffNameLength} characters.");
        }

        var reason = staff.Reason?.Trim() ?? string.Empty;
        if (reason.Length < MinReasonLength || reason.Length > MaxReasonLength)
        {
            return Error.Validation("STAFF_REASON_INVALID", $"Give a reason of {MinReasonLength} to {MaxReasonLength} characters.");
        }

        if (staff.IdempotencyKey == Guid.Empty)
        {
            return Error.Validation("IDEMPOTENCY_KEY_INVALID", "Send a fresh idempotency key with each change.");
        }

        return null;
    }

    private async Task<Result<StaffActionResponse>> ReplayAfterRaceAsync(StaffChange staff, StaffAction action, CancellationToken cancellationToken)
    {
        var earlier = await audit.FindByIdempotencyKeyAsync(staff.IdempotencyKey, cancellationToken)
            ?? throw new InvalidOperationException($"Idempotency key {staff.IdempotencyKey} was taken, but no staff audit row holds it.");
        return Replay(earlier, action);
    }

    private static Result<StaffActionResponse> Replay(StaffAuditRow earlier, StaffAction action)
    {
        if (earlier.Action != action.Name || earlier.TargetRefId != action.TargetRefId)
        {
            return Error.Conflict("IDEMPOTENCY_KEY_REUSED", "That idempotency key was already used for a different change.");
        }

        if (earlier.Outcome == StaffOutcome.Refused)
        {
            return Error.Conflict("STAFF_ACTION_REFUSED_BEFORE", $"This change was refused the first time ({earlier.Detail}). Send it again with a new idempotency key.");
        }

        return new StaffActionResponse(earlier.Id, true);
    }
}
