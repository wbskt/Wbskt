using System.Transactions;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Host.Tests.AuthHost;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;

namespace Wbskt.Host.Tests.Infrastructure;

public sealed class StaffActionRunnerTests
{
    private static readonly Guid TenantRef = Guid.NewGuid();
    private static readonly StaffAction SetLimits = new("SetTenantLimits", "Account", TenantRef);

    private readonly RecordingAudit _audit = new();
    private readonly StaffActionRunner _runner;

    public StaffActionRunnerTests()
    {
        _runner = new StaffActionRunner(_audit, NullLogger<StaffActionRunner>.Instance);
    }

    private static StaffChange Staff(string reason = "Customer asked on ticket 4411", Guid? key = null) =>
        new(Guid.NewGuid(), "pika", reason, key ?? Guid.NewGuid());

    [Theory]
    [InlineData("too short")]
    [InlineData("          padded         ")]
    public async Task A_reason_under_ten_characters_is_refused_before_anything_runs(string reason)
    {
        var ran = false;

        var result = await _runner.RunAsync(Staff(reason), SetLimits, _ => { ran = true; return Task.FromResult(Result.Success()); }, CancellationToken.None);

        Assert.Equal("STAFF_REASON_INVALID", result.Error.Code);
        Assert.False(ran);
        Assert.Empty(_audit.Rows);
    }

    [Fact]
    public async Task An_empty_idempotency_key_is_refused()
    {
        var result = await _runner.RunAsync(Staff(key: Guid.Empty), SetLimits, _ => Task.FromResult(Result.Success()), CancellationToken.None);

        Assert.Equal("IDEMPOTENCY_KEY_INVALID", result.Error.Code);
    }

    [Fact]
    public async Task A_change_and_its_row_commit_in_one_transaction()
    {
        string? changeTransaction = null;

        var result = await _runner.RunAsync(Staff(), SetLimits, _ =>
        {
            changeTransaction = Transaction.Current?.TransactionInformation.LocalIdentifier;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        Assert.False(result.Value.Replayed);
        var row = Assert.Single(_audit.Rows);
        Assert.Equal(StaffOutcome.Succeeded, row.Entry.Outcome);
        Assert.NotNull(changeTransaction);
        Assert.Equal(changeTransaction, row.TransactionId);
        Assert.Equal(TransactionStatus.Committed, row.Status);
    }

    [Fact]
    public async Task A_refused_change_rolls_back_and_is_recorded_on_its_own()
    {
        var result = await _runner.RunAsync(
            Staff("  Customer asked on ticket 4411  "),
            SetLimits,
            _ => Task.FromResult(Result.Failure(Error.Conflict("PLAN_REVISION_STALE", "stale"))),
            CancellationToken.None);

        Assert.Equal("PLAN_REVISION_STALE", result.Error.Code);
        Assert.Collection(_audit.Rows,
            first => Assert.Equal(TransactionStatus.Aborted, first.Status),
            refused =>
            {
                Assert.Equal(StaffOutcome.Refused, refused.Entry.Outcome);
                Assert.Equal("PLAN_REVISION_STALE", refused.Entry.Detail);
                Assert.Equal("Customer asked on ticket 4411", refused.Entry.Change.Reason);
                Assert.Null(refused.TransactionId);
            });
    }

    [Fact]
    public async Task A_retry_of_a_change_that_succeeded_does_not_run_it_again()
    {
        var staff = Staff();
        await _runner.RunAsync(staff, SetLimits, _ => Task.FromResult(Result.Success()), CancellationToken.None);
        var runs = 0;

        var retry = await _runner.RunAsync(staff, SetLimits, _ => { runs++; return Task.FromResult(Result.Success()); }, CancellationToken.None);

        Assert.True(retry.Value.Replayed);
        Assert.Equal(_audit.Rows[0].Id, retry.Value.AuditId);
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task A_retry_of_a_refused_change_says_so()
    {
        var staff = Staff();
        await _runner.RunAsync(staff, SetLimits, _ => Task.FromResult(Result.Failure(Error.Validation("PLAN_LIMIT_INVALID", "no"))), CancellationToken.None);

        var retry = await _runner.RunAsync(staff, SetLimits, _ => Task.FromResult(Result.Success()), CancellationToken.None);

        Assert.Equal("STAFF_ACTION_REFUSED_BEFORE", retry.Error.Code);
    }

    [Fact]
    public async Task A_key_used_for_another_change_is_refused()
    {
        var staff = Staff();
        await _runner.RunAsync(staff, SetLimits, _ => Task.FromResult(Result.Success()), CancellationToken.None);

        var other = await _runner.RunAsync(staff, SetLimits with { TargetRefId = Guid.NewGuid() }, _ => Task.FromResult(Result.Success()), CancellationToken.None);

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", other.Error.Code);
    }

    [Fact]
    public async Task Losing_a_race_on_the_key_answers_with_the_winners_row()
    {
        var staff = Staff();
        _audit.RaceWinner = new StaffAuditRow(42, SetLimits.Name, TenantRef, StaffOutcome.Succeeded, null);
        var runs = 0;

        var result = await _runner.RunAsync(staff, SetLimits, _ => { runs++; return Task.FromResult(Result.Success()); }, CancellationToken.None);

        Assert.Equal(new StaffActionResponse(42, true), result.Value);
        Assert.Equal(0, runs);
    }

    private sealed record WrittenRow(long Id, StaffAuditEntry Entry, string? TransactionId)
    {
        public TransactionStatus? Status { get; set; }
    }

    /// <summary>Keeps the rows in memory and notes the transaction each was written in and how it ended.</summary>
    private sealed class RecordingAudit : IStaffAuditProvider
    {
        public List<WrittenRow> Rows { get; } = [];

        /// <summary>When set, the first insert fails on the key as if this row had just been committed.</summary>
        public StaffAuditRow? RaceWinner { get; set; }

        public Task<StaffAuditRow?> FindByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken)
        {
            var row = Rows.FirstOrDefault(r => r.Entry.Change.IdempotencyKey == idempotencyKey && r.Status != TransactionStatus.Aborted);
            if (row is null)
            {
                return Task.FromResult(RaceWinner);
            }

            var e = row.Entry;
            return Task.FromResult<StaffAuditRow?>(new StaffAuditRow(row.Id, e.Action.Name, e.Action.TargetRefId, e.Outcome, e.Detail));
        }

        public Task<long> InsertAsync(StaffAuditEntry entry, CancellationToken cancellationToken)
        {
            if (RaceWinner is not null)
            {
                throw SqlExceptions.WithNumber(2627, "Violation of UNIQUE KEY constraint 'UQ_StaffAudit_IdempotencyKey'.");
            }

            var transaction = Transaction.Current;
            var row = new WrittenRow(Rows.Count + 1, entry, transaction?.TransactionInformation.LocalIdentifier);
            if (transaction is not null)
            {
                transaction.TransactionCompleted += (_, e) => row.Status = e.Transaction!.TransactionInformation.Status;
            }

            Rows.Add(row);
            return Task.FromResult(row.Id);
        }
    }
}
