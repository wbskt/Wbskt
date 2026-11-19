using System.Data;
using Microsoft.Extensions.Configuration;

namespace Wbskt.Infrastructure.StaffActions;

public sealed class StaffAuditProvider(IConfiguration configuration, string connectionStringName)
    : BaseSqlProvider(configuration, connectionStringName), IStaffAuditProvider
{
    public Task<StaffAuditRow?> FindByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken)
    {
        return ExecuteFindAsync(
            "dbo.StaffAudit_FindBy_IdempotencyKey",
            p => p.Add("@IdempotencyKey", SqlDbType.UniqueIdentifier).Value = idempotencyKey,
            r => new StaffAuditRow(
                r.GetInt64(r.GetOrdinal("Id")),
                r.GetString(r.GetOrdinal("Action")),
                r.GetNullableGuid("TargetRefId"),
                r.GetNullableGuid("SubjectRefId"),
                Enum.Parse<StaffOutcome>(r.GetString(r.GetOrdinal("Outcome"))),
                r.GetNullableString("Detail"),
                r.IsDBNull(r.GetOrdinal("BodyHash")) ? null : (byte[])r["BodyHash"]),
            cancellationToken);
    }

    public Task<long> InsertAsync(StaffAuditEntry entry, CancellationToken cancellationToken)
    {
        return ExecuteSingleAsync(
            "dbo.StaffAudit_Insert",
            p =>
            {
                p.Add("@StaffRefId", SqlDbType.UniqueIdentifier).Value = entry.Change.StaffRefId;
                p.Add("@StaffName", SqlDbType.NVarChar, 100).Value = entry.Change.StaffName;
                p.Add("@Action", SqlDbType.NVarChar, 64).Value = entry.Action.Name;
                p.Add("@TargetKind", SqlDbType.NVarChar, 32).Value = entry.Action.TargetKind;
                p.Add("@TargetRefId", SqlDbType.UniqueIdentifier).Value = (object?)entry.Action.TargetRefId ?? DBNull.Value;
                p.Add("@Reason", SqlDbType.NVarChar, 500).Value = entry.Change.Reason;
                p.Add("@Outcome", SqlDbType.NVarChar, 16).Value = entry.Outcome.ToString();
                p.Add("@Detail", SqlDbType.NVarChar, -1).Value = (object?)entry.Detail ?? DBNull.Value;
                p.Add("@IdempotencyKey", SqlDbType.UniqueIdentifier).Value = entry.Change.IdempotencyKey;
                p.Add("@SubjectRefId", SqlDbType.UniqueIdentifier).Value = (object?)entry.Action.SubjectRefId ?? DBNull.Value;
                p.Add("@BodyHash", SqlDbType.VarBinary, 32).Value = entry.BodyHash;
            },
            r => r.GetInt64(r.GetOrdinal("Id")),
            new InvalidOperationException("StaffAudit_Insert did not return the new row's id."),
            cancellationToken);
    }

    public Task<int> DeleteExpiredAsync(DateTime beforeUtc, int take, CancellationToken cancellationToken)
    {
        return ExecuteSingleAsync(
            "dbo.StaffAudit_DeleteExpired",
            p =>
            {
                p.Add("@Before", SqlDbType.DateTime2).Value = beforeUtc;
                p.Add("@Take", SqlDbType.Int).Value = take;
            },
            r => r.GetInt32(r.GetOrdinal("Deleted")),
            new InvalidOperationException("StaffAudit_DeleteExpired did not return a count."),
            cancellationToken);
    }
}
